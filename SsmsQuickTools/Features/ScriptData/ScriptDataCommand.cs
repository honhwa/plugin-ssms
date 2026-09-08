using System;
using System.ComponentModel.Design;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;
using SsmsQuickTools.Ssms;

namespace SsmsQuickTools.Features.ScriptData
{
    /// <summary>
    /// Comando "Copiar resultado como script SELECT": lee el grid activo (o el portapapeles
    /// como fallback), arma el script con <see cref="ValuesScriptBuilder"/> y lo deja en el
    /// portapapeles listo para pegar y ejecutar.
    /// </summary>
    public sealed class ScriptDataCommand
    {
        private const int RowLimitWithoutConfirmation = 1000;

        private readonly AsyncPackage _package;
        private readonly OleMenuCommandService _commandService;

        public ScriptDataCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _package = package;
            _commandService = commandService;
        }

        public void Register()
        {
            var commandId = new CommandID(PackageGuids.QuickToolsCmdSet, (int)PkgCmdId.ScriptDataCommand);
            var command = new OleMenuCommand(Execute, commandId);
            _commandService.AddCommand(command);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var data = TryReadResultSet(out var reason);
            if (data == null)
            {
                MessageBox.Show(reason, "SSMS Quick Tools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (data.Rows.Count > RowLimitWithoutConfirmation)
            {
                var confirm = MessageBox.Show(
                    $"El resultado tiene {data.Rows.Count} filas. Generar el script puede tardar y producir un texto grande. ¿Continuar?",
                    "SSMS Quick Tools",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (confirm != DialogResult.Yes)
                {
                    return;
                }
            }

            string script;
            try
            {
                script = ValuesScriptBuilder.Build(data.Columns, data.Rows);
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo generar el script: " + ex.Message, "SSMS Quick Tools", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                Clipboard.SetText(script, TextDataFormat.UnicodeText);
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo copiar al portapapeles: " + ex.Message, "SSMS Quick Tools", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }

        private static ResultSetData TryReadResultSet(out string reason)
        {
            var gridResult = new GridReader().TryRead(out var gridFailure);
            if (gridResult != null)
            {
                reason = null;
                return gridResult;
            }

            var clipboardResult = new ClipboardTsvReader().TryRead(out var clipboardFailure);
            if (clipboardResult != null)
            {
                reason = null;
                return clipboardResult;
            }

            reason = gridFailure + Environment.NewLine + clipboardFailure;
            return null;
        }
    }
}
