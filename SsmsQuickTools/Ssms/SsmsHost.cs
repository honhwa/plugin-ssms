using System;
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
    /// Punto unico de acceso a las APIs internas de SSMS (SQLEditors.dll / SqlWorkbench.Interfaces.dll).
    /// Todas las llamadas aqui usan tipos verificados contra SSMS 22.6.11806.211; SSMS no da soporte
    /// oficial a esta superficie y puede cambiar entre versiones (ver docs/PLAN.md).
    /// </summary>
    public static class SsmsHost
    {
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
        /// Cambia la conexion de la ventana de query activa (Quick Connect). Requiere que la
        /// vista de documento activa implemente <see cref="ISqlScriptWindowWithConnection"/>,
        /// lo que es cierto para las ventanas de query de SSMS.
        /// </summary>
        public static bool TrySetActiveWindowConnection(UIConnectionInfo connectionInfo, out string failureReason)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var docView = GetActiveDocView();
            if (docView == null)
            {
                failureReason = "No hay una ventana de query activa.";
                return false;
            }

            if (!(docView is ISqlScriptWindowWithConnection scriptWindow))
            {
                failureReason = "La ventana activa no es una ventana de query (no admite cambio de conexion).";
                return false;
            }

            try
            {
                scriptWindow.SetConnection(connectionInfo);
                failureReason = null;
                return true;
            }
            catch (Exception ex)
            {
                failureReason = "No se pudo cambiar la conexion: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Abre una nueva ventana de query en blanco, conectada con la misma conexion activa
        /// (o sin conectar si no hay ninguna), e inserta el texto dado.
        /// </summary>
        public static void OpenNewScriptWindow(string text, UIConnectionInfo connectionInfo)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (connectionInfo != null)
            {
                ScriptFactory.Instance.CreateNewBlankScript(ScriptType.Sql, connectionInfo, null);
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

            var textManager = ServiceProvider.GlobalProvider.GetService(typeof(SVsTextManager)) as IVsTextManager;
            if (textManager == null)
            {
                return null;
            }

            if (ErrorHandler.Failed(textManager.GetActiveView(1, null, out var view)) || view == null)
            {
                return null;
            }

            if (ErrorHandler.Failed(view.GetSelectedText(out var text)))
            {
                return null;
            }

            return text;
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
