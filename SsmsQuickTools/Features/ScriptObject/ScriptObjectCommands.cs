using System;
using System.ComponentModel.Design;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;
using SsmsQuickTools.Ssms;

namespace SsmsQuickTools.Features.ScriptObject
{
    /// <summary>
    /// Menu contextual del editor: "Generar CREATE" / "Generar ALTER" sobre el nombre de un
    /// objeto seleccionado (o la palabra bajo el cursor).
    /// </summary>
    public sealed class ScriptObjectCommands
    {
        private readonly AsyncPackage _package;
        private readonly OleMenuCommandService _commandService;

        public ScriptObjectCommands(AsyncPackage package, OleMenuCommandService commandService)
        {
            _package = package;
            _commandService = commandService;
        }

        public void Register()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            AddCommand(PkgCmdId.GenerateCreateCommand, (s, e) => Execute(wantAlter: false));
            AddCommand(PkgCmdId.GenerateAlterCommand, (s, e) => Execute(wantAlter: true));
        }

        private void AddCommand(uint id, EventHandler handler)
        {
            var commandId = new CommandID(PackageGuids.EditorContextCmdSet, (int)id);
            var command = new OleMenuCommand(handler, commandId);
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

            // Visible solo si hay una ventana de query conectada y se puede leer un nombre de
            // objeto del texto activo. La verificacion de si el objeto existe (y si admite
            // ALTER, ej. las tablas no) se hace recien al ejecutar, para no pegarle a la base
            // de datos en cada apertura del menu contextual.
            // Visible solo si hay una ventana de query conectada y se puede leer un nombre de
            // objeto del texto activo. La verificacion de si el objeto existe (y si admite
            // ALTER, ej. las tablas no) se hace recien al ejecutar, para no pegarle a la base
            // de datos en cada apertura del menu contextual.
            var connectionInfo = SsmsHost.GetActiveConnectionInfo();
            var candidateText = GetCandidateText();
            var parsed = ObjectNameParser.Parse(candidateText);

            command.Visible = connectionInfo != null && parsed != null;
            command.Enabled = command.Visible;
        }

        private void Execute(bool wantAlter)
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

            var database = parsed.Database ?? connectionInfo.AdvancedOptions["DATABASE"];
            string connectionString;
            try
            {
                connectionString = SsmsHost.BuildConnectionString(connectionInfo, database);
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo preparar la conexion: " + ex.Message, "SSMS Quick Tools", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var result = wantAlter
                ? ObjectScripter.GenerateAlter(parsed, connectionString, out var error)
                : ObjectScripter.GenerateCreate(parsed, connectionString, out error);

            if (result == null)
            {
                MessageBox.Show(error, "SSMS Quick Tools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                Clipboard.SetText(result.Sql, TextDataFormat.UnicodeText);
            }
            catch (Exception)
            {
                // no bloquea el flujo si el portapapeles esta ocupado por otro proceso
            }

            SsmsHost.OpenNewScriptWindow(result.Sql, connectionInfo, null);
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
