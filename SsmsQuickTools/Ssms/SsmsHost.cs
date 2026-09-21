using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management.Smo.RegSvrEnum;
using Microsoft.SqlServer.Management.UI.VSIntegration.Editors;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;

namespace SsmsQuickTools.Ssms
{
    /// <summary>
    /// Resultado de <see cref="SsmsHost.TryReconnectActiveWindow"/>.
    /// </summary>
    public enum ReconnectOutcome
    {
        /// <summary>Mismo servidor: solo se cambio la base de datos de la ventana activa.</summary>
        DatabaseChanged,

        /// <summary>Servidor distinto (o ventana sin conectar): la ventana activa quedo reconectada.</summary>
        Reconnected,

        /// <summary>No hay una ventana de query activa; el llamador debe abrir una nueva.</summary>
        NoQueryWindow,

        /// <summary>Hay una consulta en ejecucion o una transaccion abierta; no se toco la conexion.</summary>
        Blocked,

        /// <summary>Fallo al reconectar (motivo en el parametro de salida).</summary>
        Failed,
    }

    /// <summary>
    /// Punto unico de acceso a las APIs internas de SSMS (SQLEditors.dll / SqlWorkbench.Interfaces.dll).
    /// Todas las llamadas aqui usan tipos verificados contra SSMS 22.6.11806.211; SSMS no da soporte
    /// oficial a esta superficie y puede cambiar entre versiones (ver docs/PLAN.md).
    /// </summary>
    public static class SsmsHost
    {
        private const BindingFlags InstanceAny = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;


        /// <summary>
        /// Informacion de conexion de la ventana de query activa, o null si no hay ninguna
        /// ventana activa o no esta conectada.
        /// </summary>
        public static UIConnectionInfo GetActiveConnectionInfo()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var current = ScriptFactory.Instance?.CurrentlyActiveWndConnectionInfo;
            if (current == null || !current.Live)
            {
                return null;
            }

            return current.UIConnectionInfo;
        }

        /// <summary>
        /// Reconecta in-place la ventana de query activa a <paramref name="target"/>/<paramref name="database"/>
        /// (Quick Connect, Milestone 1). Si la ventana activa esta conectada al mismo servidor, solo
        /// cambia la base (propiedad publica <c>CurrentDB</c> de <c>SqlScriptEditorControl</c>, la
        /// misma via que usa el combo de bases nativo de SSMS). Si es otro servidor, desconecta y
        /// reconecta la ventana (<c>Disconnect()</c> + <c>ISqlScriptWindowWithConnection.SetConnection</c>).
        /// No reemplaza ninguna ventana existente por una nueva: eso queda a cargo del llamador cuando
        /// el resultado es <see cref="ReconnectOutcome.NoQueryWindow"/>.
        /// </summary>
        /// <param name="target">Conexion destino (servidor + autenticacion).</param>
        /// <param name="database">Base de datos destino.</param>
        /// <param name="openConnection">
        /// Abre una conexion ADO.NET nueva ya conectada a <paramref name="database"/>. Solo se invoca
        /// cuando hace falta reconectar por completo (servidor distinto o ventana sin conectar); el
        /// caso "misma base, mismo servidor" no abre ninguna conexion. La conexion que devuelva queda
        /// en poder de este metodo: si la reconexion tiene exito la adopta la ventana de SSMS, y si
        /// falla se descarta (Dispose) antes de retornar.
        /// </param>
        public static ReconnectOutcome TryReconnectActiveWindow(UIConnectionInfo target, string database, Func<IDbConnection> openConnection, out string reason)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            reason = null;

            var docView = GetActiveDocView();
            if (!(docView is ISqlScriptWindowWithConnection scriptWindow))
            {
                return ReconnectOutcome.NoQueryWindow;
            }

            var windowType = docView.GetType();

            // Guarda: consulta en ejecucion. IsExecuting es "family" (protected) en la clase base
            // ScriptAndResultsEditorControl; SqlScriptEditorControl no la sobreescribe.
            if (TryGetInheritedPropertyValue(docView, windowType, "IsExecuting", out bool isExecuting) && isExecuting)
            {
                reason = "Hay una consulta en ejecucion en la ventana activa.";
                return ReconnectOutcome.Blocked;
            }

            var connectionState = docView as Microsoft.SqlServer.Management.UI.VSIntegration.ISqlToolsWindowWithConnectionState;
            var isConnected = connectionState?.IsConnected ?? false;
            var activeInfo = connectionState?.Connection;

            // Guarda: transaccion abierta. Se consulta @@TRANCOUNT directamente sobre la conexion
            // activa (campo "m_connection", declarado en ScriptAndResultsEditorControl) en lugar de
            // usar los metodos de transaccion de SSMS, que abren dialogos de commit/rollback.
            if (isConnected && TryGetInheritedFieldValue(docView, windowType, "m_connection", out IDbConnection activeConnection)
                && activeConnection != null && activeConnection.State == ConnectionState.Open
                && HasOpenTransaction(activeConnection))
            {
                reason = "La ventana activa tiene una transaccion abierta.";
                return ReconnectOutcome.Blocked;
            }

            var sameServer = isConnected && activeInfo != null
                && string.Equals(activeInfo.ServerName, target.ServerName, StringComparison.OrdinalIgnoreCase)
                && activeInfo.AuthenticationType == target.AuthenticationType;

            if (sameServer)
            {
                if (TrySetInheritedPropertyValue(docView, windowType, "CurrentDB", database, out var setError))
                {
                    return ReconnectOutcome.DatabaseChanged;
                }

                reason = "No se pudo cambiar la base de datos: " + setError;
                return ReconnectOutcome.Failed;
            }

            IDbConnection dbConnection;
            try
            {
                dbConnection = openConnection();
            }
            catch (Exception ex)
            {
                reason = "No se pudo abrir la conexion: " + ex.Message;
                return ReconnectOutcome.Failed;
            }

            try
            {
                if (isConnected && !TryInvokeInheritedMethod(docView, windowType, "Disconnect", Array.Empty<object>(), out var disconnectError))
                {
                    reason = "No se pudo desconectar la ventana activa: " + disconnectError;
                    dbConnection.Dispose();
                    return ReconnectOutcome.Failed;
                }

                // A partir de aqui SetConnection es duena de dbConnection (igual que en el codigo
                // que reemplaza a este metodo); si falla, no quedo adoptada por nadie mas.
                scriptWindow.SetConnection(target, dbConnection);
            }
            catch (Exception ex)
            {
                reason = "No se pudo reconectar la ventana activa: " + ex.Message;
                dbConnection.Dispose();
                return ReconnectOutcome.Failed;
            }

            // Mejor esfuerzo: refresca barra de estado, combo de base y opciones de ejecucion, igual
            // que hace SSMS tras conectar desde su propio dialogo. Si el miembro no existe o falla,
            // la conexion ya quedo puesta; no se revierte nada.
            if (TryGetInheritedFieldValue(docView, windowType, "m_connectionInfoList", out object groupInfo)
                && TryGetInheritedFieldValue(docView, windowType, "m_connection", out object adoptedConnection))
            {
                TryInvokeInheritedMethod(docView, windowType, "OnScriptGotNewConnection", new[] { groupInfo, adoptedConnection }, out _);
            }

            return ReconnectOutcome.Reconnected;
        }

        private static bool HasOpenTransaction(IDbConnection connection)
        {
            try
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT @@TRANCOUNT";
                    var result = command.ExecuteScalar();
                    return result != null && Convert.ToInt32(result) > 0;
                }
            }
            catch
            {
                // Si no se puede consultar (conexion inestable, etc.) no se bloquea la reconexion
                // por esto; otras guardas/el intento de reconexion en si detectaran el problema.
                return false;
            }
        }

        /// <summary>
        /// Busca un tipo (o su cadena de tipos base) hasta encontrar uno que declare el miembro
        /// buscado. Reflection sobre un miembro no publico solo lo encuentra si se pregunta al tipo
        /// que lo declara, no a un tipo derivado.
        /// </summary>
        private static Type FindDeclaringType(Type type, Func<Type, bool> declaresMember)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                if (declaresMember(t))
                {
                    return t;
                }
            }

            return null;
        }

        private static bool TryGetInheritedFieldValue<T>(object instance, Type type, string fieldName, out T value)
        {
            try
            {
                var declaringType = FindDeclaringType(type, t => t.GetField(fieldName, InstanceAny | BindingFlags.DeclaredOnly) != null);
                var field = declaringType?.GetField(fieldName, InstanceAny | BindingFlags.DeclaredOnly);
                if (field != null && field.GetValue(instance) is T typed)
                {
                    value = typed;
                    return true;
                }
            }
            catch
            {
                // Miembro ausente o cambiado de forma en esta version de SSMS: se trata como "no
                // disponible" en vez de propagar la excepcion.
            }

            value = default;
            return false;
        }

        private static bool TryGetInheritedPropertyValue<T>(object instance, Type type, string propertyName, out T value)
        {
            try
            {
                var declaringType = FindDeclaringType(type, t => t.GetProperty(propertyName, InstanceAny | BindingFlags.DeclaredOnly) != null);
                var property = declaringType?.GetProperty(propertyName, InstanceAny | BindingFlags.DeclaredOnly);
                if (property != null && property.GetValue(instance) is T typed)
                {
                    value = typed;
                    return true;
                }
            }
            catch
            {
            }

            value = default;
            return false;
        }

        private static bool TrySetInheritedPropertyValue(object instance, Type type, string propertyName, object value, out string error)
        {
            try
            {
                var declaringType = FindDeclaringType(type, t => t.GetProperty(propertyName, InstanceAny | BindingFlags.DeclaredOnly) != null);
                var property = declaringType?.GetProperty(propertyName, InstanceAny | BindingFlags.DeclaredOnly);
                if (property == null)
                {
                    error = "propiedad '" + propertyName + "' no encontrada.";
                    return false;
                }

                property.SetValue(instance, value);
                error = null;
                return true;
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                error = ex.InnerException.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool TryInvokeInheritedMethod(object instance, Type type, string methodName, object[] args, out string error)
        {
            try
            {
                var declaringType = FindDeclaringType(type, t => t.GetMethod(methodName, InstanceAny | BindingFlags.DeclaredOnly) != null);
                var method = declaringType?.GetMethod(methodName, InstanceAny | BindingFlags.DeclaredOnly);
                if (method == null)
                {
                    error = "metodo '" + methodName + "' no encontrado.";
                    return false;
                }

                method.Invoke(instance, args);
                error = null;
                return true;
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                error = ex.InnerException.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Invoca el overload sin parametros de un metodo heredado, cuando el tipo declara mas de
        /// un overload con ese nombre (<c>GetMethod(string, BindingFlags)</c> lanza
        /// <see cref="AmbiguousMatchException"/> en ese caso, a diferencia de
        /// <see cref="TryInvokeInheritedMethod"/>). Mejor esfuerzo: si falla, no hace nada.
        /// </summary>
        private static void InvokeParameterlessInheritedMethod(object instance, Type type, string methodName)
        {
            try
            {
                var declaringType = FindDeclaringType(type, t => Array.Exists(
                    t.GetMethods(InstanceAny | BindingFlags.DeclaredOnly),
                    m => m.Name == methodName && m.GetParameters().Length == 0));
                var method = declaringType?.GetMethods(InstanceAny | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length == 0);
                method?.Invoke(instance, Array.Empty<object>());
            }
            catch
            {
                // Mejor esfuerzo: si no se pudo forzar la expansion, EnsureChildrenLoaded lo
                // trata igual que "no cargo a tiempo" (la busqueda de arriba simplemente no
                // encuentra el objeto y cae al flujo de "no encontrado").
            }
        }

        /// <summary>
        /// Abre una nueva ventana de query en blanco, conectada con la misma conexion activa
        /// (o sin conectar si no hay ninguna), e inserta el texto dado.
        /// </summary>
        public static void OpenNewScriptWindow(string text, UIConnectionInfo connectionInfo, System.Data.IDbConnection dbConnection)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (connectionInfo != null)
            {
                ScriptFactory.Instance.CreateNewBlankScript(ScriptType.Sql, connectionInfo, dbConnection);
            }
            else
            {
                ScriptFactory.Instance.CreateNewBlankScript(ScriptType.Sql);
            }

            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            // La ventana recien creada queda como activa; se inserta el texto en ella.
            InsertTextIntoActiveView(text);
        }

        /// <summary>
        /// Inserta texto en la vista de texto activa (reemplaza la seleccion actual, o inserta
        /// en la posicion del cursor si no hay seleccion).
        /// </summary>
        public static void InsertTextIntoActiveView(string text)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var textManager = ServiceProvider.GlobalProvider.GetService(typeof(SVsTextManager)) as IVsTextManager;
            if (textManager == null)
            {
                return;
            }

            if (ErrorHandler.Failed(textManager.GetActiveView(1, null, out var view)) || view == null)
            {
                return;
            }

            if (ErrorHandler.Failed(view.GetBuffer(out var buffer)) || buffer == null)
            {
                return;
            }

            view.GetSelection(out var startLine, out var startCol, out var endLine, out var endCol);

            var textPtr = System.Runtime.InteropServices.Marshal.StringToCoTaskMemUni(text);
            try
            {
                buffer.ReplaceLines(startLine, startCol, endLine, endCol, textPtr, text.Length, new TextSpan[1]);
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FreeCoTaskMem(textPtr);
            }
        }

        /// <summary>
        /// Texto seleccionado en el editor activo, o null si no hay editor activo.
        /// </summary>
        public static string GetActiveSelectedText()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var view = GetActiveTextView();
            if (view == null)
            {
                return null;
            }

            if (ErrorHandler.Failed(view.GetSelectedText(out var text)))
            {
                return null;
            }

            return text;
        }

        /// <summary>
        /// Palabra (nombre de objeto candidato) bajo el cursor en el editor activo, o null si no
        /// hay editor activo o el caret no esta sobre texto util. Usado como fallback cuando no
        /// hay seleccion explicita (Milestone 3).
        /// </summary>
        public static string GetWordUnderCursor()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var view = GetActiveTextView();
            if (view == null)
            {
                return null;
            }

            if (ErrorHandler.Failed(view.GetCaretPos(out var line, out var column)))
            {
                return null;
            }

            if (ErrorHandler.Failed(view.GetBuffer(out var buffer)) || buffer == null)
            {
                return null;
            }

            if (ErrorHandler.Failed(buffer.GetLengthOfLine(line, out var lineLength)))
            {
                return null;
            }

            if (ErrorHandler.Failed(buffer.GetLineText(line, 0, line, lineLength, out var lineText)) || string.IsNullOrEmpty(lineText))
            {
                return null;
            }

            return ExtractNameTokenAt(lineText, column);
        }

        /// <summary>
        /// Extrae el token tipo "nombre de objeto SQL" (letras, digitos, '_', '@', '#', '.', '[', ']')
        /// que contiene la posicion indicada, expandiendo hacia ambos lados. Logica pura.
        /// </summary>
        private static string ExtractNameTokenAt(string lineText, int column)
        {
            bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '@' || c == '#' || c == '.' || c == '[' || c == ']';

            if (column < 0 || column > lineText.Length)
            {
                return null;
            }

            var start = column;
            var end = column;

            // Si el caret esta justo despues del token (ej. al final de la linea) o entre dos
            // tokens, preferir el caracter a la izquierda para decidir si hay algo bajo el cursor.
            if (start == lineText.Length || !IsNameChar(lineText[start]))
            {
                if (start > 0 && IsNameChar(lineText[start - 1]))
                {
                    start--;
                    end = start + 1;
                }
                else
                {
                    return null;
                }
            }

            while (start > 0 && IsNameChar(lineText[start - 1]))
            {
                start--;
            }

            while (end < lineText.Length && IsNameChar(lineText[end]))
            {
                end++;
            }

            var token = lineText.Substring(start, end - start).Trim('.');
            return string.IsNullOrEmpty(token) ? null : token;
        }

        /// <summary>
        /// Vista de texto activa (IVsTextView), o null si no hay ninguna. Publico para que
        /// AutoReplacementService pueda enganchar su filtro de comandos sobre ella.
        /// </summary>
        public static IVsTextView GetActiveTextView()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var textManager = ServiceProvider.GlobalProvider.GetService(typeof(SVsTextManager)) as IVsTextManager;
            if (textManager == null)
            {
                return null;
            }

            return ErrorHandler.Failed(textManager.GetActiveView(1, null, out var view)) ? null : view;
        }

        private static object GetActiveDocView()
        {
            var monitorSelection = ServiceProvider.GlobalProvider.GetService(typeof(SVsShellMonitorSelection)) as IVsMonitorSelection;
            if (monitorSelection == null)
            {
                return null;
            }

            monitorSelection.GetCurrentElementValue((uint)VSConstants.VSSELELEMID.SEID_WindowFrame, out var frameObj);
            if (!(frameObj is IVsWindowFrame frame))
            {
                return null;
            }

            return ErrorHandler.Succeeded(frame.GetProperty((int)__VSFPROPID.VSFPROPID_DocView, out var docView))
                ? docView
                : null;
        }

        // Cantidad de niveles del arbol de Object Explorer que se recorren para encontrar la
        // carpeta "Databases" y la base de datos buscada, y luego el objeto dentro de la base.
        // No se conocen los nombres de las carpetas intermedias (estan localizados y no hay
        // constantes publicas para ellos), asi que la busqueda no filtra por texto de carpeta:
        // expande y recorre todo lo que encuentra hasta esta profundidad, comparando solo el
        // nombre de cada nodo contra lo buscado. Ver docs/PLAN.md y specs/01 (riesgo: API de
        // Object Explorer no documentada).
        private const int LocateDatabaseMaxDepth = 2;
        private const int LocateObjectMaxDepth = 4;

        // La primera expansion de un nodo de servidor todavia no tocado por el usuario puede
        // conectar de cero (handshake + autenticacion) antes de poblar "Databases"; se vio en
        // pruebas manuales que 10s no alcanzaba ahi (si funcionaba al reintentar, ya con el
        // arbol tibio). 30s da margen sin trabar la UI de forma indefinida si la conexion
        // realmente esta caida (EnsureChildrenLoaded sigue bombeando el message loop mientras
        // espera).
        private const int EnsureChildrenLoadedTimeoutMs = 30000;

        /// <summary>
        /// Ubica y selecciona, en el arbol de Object Explorer, el objeto <paramref name="schema"/>.<paramref name="objectName"/>
        /// (o, si no aparece calificado por esquema, <paramref name="objectName"/> solo -- caso de
        /// los triggers, que en el arbol cuelgan de su tabla/vista padre sin prefijo de esquema)
        /// dentro de la base <paramref name="database"/> del servidor de <paramref name="connectionInfo"/>.
        /// Fuerza la expansion de las carpetas que el usuario todavia no desplego. Si Object
        /// Explorer no tiene un nodo para ese servidor/base, o el objeto no aparece en el arbol ya
        /// expandido, invoca <paramref name="onNotFound"/> (con el motivo puntual, para
        /// diagnostico) sin modificar nada. No conecta ni busca en otro servidor o conexion
        /// (Milestone/spec 01).
        /// </summary>
        public static void LocateObjectInObjectExplorer(UIConnectionInfo connectionInfo, string database, string schema, string objectName, Action<string> onNotFound)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var control = FindObjectExplorerControl(out var controlDiagnostic);
            if (control == null)
            {
                onNotFound("No se encontro el panel de Object Explorer (¿esta abierto?). Ventanas recorridas: " + controlDiagnostic);
                return;
            }

            var serverNode = FindImmediateChildByName(control.Nodes, connectionInfo.ServerName);
            if (serverNode == null)
            {
                onNotFound("No se encontro un nodo para el servidor \"" + connectionInfo.ServerName + "\" en Object Explorer.");
                return;
            }

            var databaseNode = FindNodeByName(serverNode, new[] { database }, LocateDatabaseMaxDepth);
            if (databaseNode == null)
            {
                onNotFound("No se encontro la base de datos \"" + database + "\" bajo ese servidor en Object Explorer.");
                return;
            }

            var candidateNames = new[] { schema + "." + objectName, objectName };
            var objectNode = FindNodeByName(databaseNode, candidateNames, LocateObjectMaxDepth);
            if (objectNode == null)
            {
                onNotFound("No se encontro el objeto dentro del arbol de esa base de datos (profundidad maxima explorada: " + LocateObjectMaxDepth + " niveles).");
                return;
            }

            // La busqueda expande todas las carpetas que recorre en el camino (no solo la que
            // termina llevando al objeto), asi que sin este paso el arbol queda con ramas de mas
            // desplegadas (ej. Seguridad, Vistas, Programacion). Se colapsa todo menos la cadena
            // de ancestros del objeto encontrado, igual que el "Locate" nativo de SSMS.
            CollapseExceptAncestorsOf(control.Nodes, objectNode);

            control.SelectedNode = objectNode;
            objectNode.EnsureVisible();
            control.Focus();
        }

        private static void CollapseExceptAncestorsOf(TreeNodeCollection roots, TreeNode target)
        {
            var ancestors = new HashSet<TreeNode>();
            for (var node = target.Parent; node != null; node = node.Parent)
            {
                ancestors.Add(node);
            }

            foreach (TreeNode root in roots)
            {
                CollapseExceptAncestorsRecursive(root, ancestors);
            }
        }

        private static void CollapseExceptAncestorsRecursive(TreeNode node, HashSet<TreeNode> ancestors)
        {
            foreach (TreeNode child in node.Nodes)
            {
                CollapseExceptAncestorsRecursive(child, ancestors);
            }

            if (ancestors.Contains(node))
            {
                node.Expand();
            }
            else
            {
                node.Collapse();
            }
        }

        // Nombre completo de Microsoft.SqlServer.Management.SqlStudio.Explorer.ObjectExplorerToolWindow,
        // el ToolWindowPane que aloja el ObjectExplorerControl en SSMS 22 (confirmado por prueba
        // manual contra SSMS 22.6.11806.211: el DocView de la tool window de Object Explorer no
        // es el control en si). Se identifica por nombre de tipo y se lee su propiedad publica
        // "Control" por reflection -- no por referencia directa a esa DLL -- porque depende de
        // Microsoft.VisualStudio.Shell.15.0 v18.0 (shell VS2022 de SSMS 22), mas nueva que el
        // Microsoft.VisualStudio.SDK 17.11 de este proyecto (referenciarla rompe la compilacion,
        // CS1705). Ver lib/ssms22.6/README.md.
        private const string ObjectExplorerToolWindowTypeName = "Microsoft.SqlServer.Management.SqlStudio.Explorer.ObjectExplorerToolWindow";

        /// <summary>
        /// Instancia activa del arbol de Object Explorer (como <see cref="TreeView"/> publico; la
        /// clase real <c>ObjectExplorerControl</c> es interna a ObjectExplorer.dll), buscando
        /// entre todas las ventanas de herramientas registradas -- no se conoce el GUID publico de
        /// la ventana de Object Explorer de SSMS.
        /// </summary>
        private static TreeView FindObjectExplorerControl() => FindObjectExplorerControl(out _);

        /// <summary>
        /// Igual que <see cref="FindObjectExplorerControl()"/>, pero ademas devuelve en
        /// <paramref name="diagnostic"/> el caption de cada tool window recorrida cuando no se
        /// encuentra el panel, para diagnosticar (ej. panel cerrado, o no registrado todavia).
        /// </summary>
        private static TreeView FindObjectExplorerControl(out string diagnostic)
        {
            var seen = new System.Text.StringBuilder();

            var uiShell = ServiceProvider.GlobalProvider.GetService(typeof(SVsUIShell)) as IVsUIShell;
            if (uiShell == null)
            {
                diagnostic = "No se pudo obtener SVsUIShell.";
                return null;
            }

            if (ErrorHandler.Failed(uiShell.GetToolWindowEnum(out var frames)) || frames == null)
            {
                diagnostic = "GetToolWindowEnum fallo o devolvio null.";
                return null;
            }

            var fetched = new IVsWindowFrame[1];
            while (frames.Next(1, fetched, out var count) == VSConstants.S_OK && count == 1)
            {
                var frame = fetched[0];
                var caption = ErrorHandler.Succeeded(frame.GetProperty((int)__VSFPROPID.VSFPROPID_Caption, out var captionObj))
                    ? captionObj as string
                    : null;
                seen.Append("[").Append(caption).Append("] ");

                if (ErrorHandler.Failed(frame.GetProperty((int)__VSFPROPID.VSFPROPID_DocView, out var docView)))
                {
                    continue;
                }

                if (docView != null && docView.GetType().FullName == ObjectExplorerToolWindowTypeName
                    && TryGetInheritedPropertyValue(docView, docView.GetType(), "Control", out TreeView control))
                {
                    diagnostic = null;
                    return control;
                }
            }

            diagnostic = seen.ToString();
            return null;
        }

        private static TreeNode FindImmediateChildByName(TreeNodeCollection nodes, string name)
        {
            foreach (TreeNode node in nodes)
            {
                if (string.Equals(NodeName(node), name, StringComparison.OrdinalIgnoreCase))
                {
                    return node;
                }
            }

            return null;
        }

        /// <summary>
        /// Busqueda en anchura por debajo de <paramref name="root"/> (sin incluirlo), hasta
        /// <paramref name="maxDepth"/> niveles, expandiendo cada carpeta lazy que todavia no fue
        /// cargada (<see cref="EnsureChildrenLoaded"/>). Devuelve el primer nodo cuyo nombre
        /// coincide (sin distinguir mayusculas) con alguno de <paramref name="candidateNames"/>.
        /// </summary>
        private static TreeNode FindNodeByName(TreeNode root, string[] candidateNames, int maxDepth)
        {
            var frontier = new Queue<KeyValuePair<TreeNode, int>>();
            frontier.Enqueue(new KeyValuePair<TreeNode, int>(root, 0));

            while (frontier.Count > 0)
            {
                var current = frontier.Dequeue();
                var node = current.Key;
                var depth = current.Value;

                if (depth > 0)
                {
                    var name = NodeName(node);
                    foreach (var candidate in candidateNames)
                    {
                        if (string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase))
                        {
                            return node;
                        }
                    }
                }

                if (depth >= maxDepth)
                {
                    continue;
                }

                EnsureChildrenLoaded(node);
                foreach (TreeNode child in node.Nodes)
                {
                    frontier.Enqueue(new KeyValuePair<TreeNode, int>(child, depth + 1));
                }
            }

            return null;
        }

        // "NodeName" es la propiedad publica de ExplorerHierarchyNode (clase interna) con el
        // nombre "crudo" del objeto (sin el icono/estado que puede llevar DisplayName); TreeNode
        // no la declara, asi que se lee por reflection y se cae a Text (siempre disponible, es
        // publica en TreeNode) si el nodo no es de ese tipo o la propiedad no esta.
        private static string NodeName(TreeNode node) =>
            TryGetInheritedPropertyValue(node, node.GetType(), "NodeName", out string name) ? name : node.Text;

        /// <summary>
        /// Fuerza la carga de los hijos de un nodo todavia no expandido por el usuario
        /// (metodo publico <c>EnumerateChildren()</c> de ExplorerHierarchyNode, clase interna),
        /// y espera -bombeando el message loop, dado que la carga corre en otro hilo y notifica
        /// de vuelta al hilo de UI a que termine- con un limite de tiempo para no colgar la UI si
        /// algo se queda esperando una conexion caida.
        /// </summary>
        private static void EnsureChildrenLoaded(TreeNode node)
        {
            if (!TryGetInheritedPropertyValue(node, node.GetType(), "ChildrenEnumerated", out bool alreadyLoaded) || alreadyLoaded)
            {
                return;
            }

            InvokeParameterlessInheritedMethod(node, node.GetType(), "EnumerateChildren");

            var deadline = Environment.TickCount + EnsureChildrenLoadedTimeoutMs;
            while (TryGetInheritedPropertyValue(node, node.GetType(), "ChildrenEnumerated", out bool loaded) && !loaded && Environment.TickCount < deadline)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(15);
            }
        }

        /// <summary>
        /// Construye una cadena de conexion ADO.NET independiente para consultas puntuales
        /// (Milestone 3: OBJECT_DEFINITION / SMO). Solo cubre Windows y SQL Authentication;
        /// para Microsoft Entra ID el usuario debera copiar el script y ejecutarlo el mismo
        /// desde una ventana ya conectada.
        /// </summary>
        public static string BuildConnectionString(UIConnectionInfo info, string database)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            var builder = new SqlConnectionStringBuilder
            {
                DataSource = info.ServerName,
                ApplicationName = "SsmsQuickTools",
                ConnectTimeout = 15,
                // Microsoft.Data.SqlClient exige Encrypt=true por defecto; instancias locales/dev
                // sin certificado valido fallan con "cadena de certificacion no confiable" si no
                // se confia explicitamente en el certificado del servidor.
                TrustServerCertificate = true,
            };

            if (!string.IsNullOrEmpty(database))
            {
                builder.InitialCatalog = database;
            }

            // AuthenticationType: 0 = Windows, 1 = SQL Server. Otros valores (Entra ID en sus
            // variantes) no se soportan aqui; se cae a Windows como mejor esfuerzo.
            if (info.AuthenticationType == 1 && !string.IsNullOrEmpty(info.UserName))
            {
                builder.UserID = info.UserName;
                builder.Password = info.Password ?? string.Empty;
            }
            else
            {
                builder.IntegratedSecurity = true;
            }

            return builder.ConnectionString;
        }
    }
}
