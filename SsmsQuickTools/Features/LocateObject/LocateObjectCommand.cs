using System;
using System.ComponentModel.Design;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;
using SsmsQuickTools.Features.ScriptObject;
using SsmsQuickTools.Ssms;

namespace SsmsQuickTools.Features.LocateObject
{
    /// <summary>
    /// Menu Quick Tools &gt; Query: "Locate in Object Explorer" sobre el nombre de un objeto
    /// seleccionado (o la palabra bajo el cursor). Mismo origen de texto y parseo que
    /// ScriptObjectCommands (spec 01).
    /// </summary>
    public sealed class LocateObjectCommand
    {
        private readonly AsyncPackage _package;
        private readonly OleMenuCommandService _commandService;

        public LocateObjectCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _package = package;
            _commandService = commandService;
        }

        public void Register()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var commandId = new CommandID(PackageGuids.QuickToolsCmdSet, (int)PkgCmdId.LocateObjectCommand);
            var command = new OleMenuCommand(Execute, commandId);
            command.BeforeQueryStatus += OnBeforeQueryStatus;
            _commandService.AddCommand(command);
        }

        private void OnBeforeQueryStatus(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!(sender is OleMenuCommand command))
            {
                return;
            }

            // Mismo criterio que Script CREATE/ALTER: visible solo si hay ventana de query
            // conectada y se puede leer un nombre de objeto candidato del texto activo. No se
            // verifica si el objeto existe en el arbol recien al abrir el menu, para no pegarle
            // a Object Explorer (que puede forzar expansiones) en cada apertura.
            var connectionInfo = SsmsHost.GetActiveConnectionInfo();
            var candidateText = GetCandidateText();
            var parsed = ObjectNameParser.Parse(candidateText);

            command.Visible = connectionInfo != null && parsed != null;
            command.Enabled = command.Visible;
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var connectionInfo = SsmsHost.GetActiveConnectionInfo();
            if (connectionInfo == null)
            {
                MessageBox.Show("No hay una ventana de query conectada.", "SSMS Quick Tools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var candidateText = GetCandidateText();
            var parsed = ObjectNameParser.Parse(candidateText);
            if (parsed == null)
            {
                MessageBox.Show("Selecciona (o ubica el cursor sobre) el nombre de un objeto.", "SSMS Quick Tools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // No busca en otro servidor/base de datos distinto al de la conexion activa: si el
            // objeto viene calificado con una base sin nodo abierto en Object Explorer, cae en
            // el mismo flujo de "no encontrado" (spec 01, fuera de alcance: autoconectar).
            var database = parsed.Database ?? connectionInfo.AdvancedOptions["DATABASE"];

            SsmsHost.LocateObjectInObjectExplorer(connectionInfo, database, parsed.Schema, parsed.Name, reason =>
            {
                MessageBox.Show(
                    "No se encontro \"" + parsed.QuotedSchemaQualifiedName + "\" en el arbol de Object Explorer.\n\n" + reason,
                    "SSMS Quick Tools",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            });
        }

        /// <summary>
        /// Texto seleccionado en el editor, o (si no hay seleccion) la palabra bajo el cursor.
        /// </summary>
        private static string GetCandidateText()
        {
            var selected = SsmsHost.GetActiveSelectedText();
            return string.IsNullOrWhiteSpace(selected) ? SsmsHost.GetWordUnderCursor() : selected;
        }
    }
}
