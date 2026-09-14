using System;
using System.ComponentModel.Design;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;
using SsmsQuickTools.Ssms;

namespace SsmsQuickTools.Features.CopyXmlSpreadsheet
{
    /// <summary>
    /// Comando "Copiar seleccion como XML Spreadsheet": lee la seleccion del grid activo (sin
    /// fallback de portapapeles: requiere seleccion explicita, ver docs/PLAN.md M4), arma el
    /// documento con <see cref="XmlSpreadsheetBuilder"/> y lo deja en el portapapeles en el
    /// formato nativo de Excel "XML Spreadsheet", con un fallback TSV para otros destinos.
    /// </summary>
    public sealed class CopyXmlSpreadsheetCommand
    {
        private const int RowLimitWithoutConfirmation = 1000;

        private readonly AsyncPackage _package;
        private readonly OleMenuCommandService _commandService;

        public CopyXmlSpreadsheetCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _package = package;
            _commandService = commandService;
        }

        public void Register()
        {
            var commandId = new CommandID(PackageGuids.QuickToolsCmdSet, (int)PkgCmdId.CopyXmlSpreadsheetCommand);
            var command = new OleMenuCommand(Execute, commandId);
            _commandService.AddCommand(command);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var data = new GridReader().TryRead(out var reason);
            if (data == null)
            {
                MessageBox.Show(reason, "SSMS Quick Tools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!data.HasSelection)
            {
                MessageBox.Show(
                    "Selecciona celdas en el grid de resultados antes de usar este comando.",
                    "SSMS Quick Tools",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (data.Rows.Count > RowLimitWithoutConfirmation)
            {
                var confirm = MessageBox.Show(
                    $"La seleccion tiene {data.Rows.Count} filas. Generar el XML Spreadsheet puede tardar y producir un texto grande. ¿Continuar?",
                    "SSMS Quick Tools",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (confirm != DialogResult.Yes)
                {
                    return;
                }
            }

            string xml;
            try
            {
                xml = XmlSpreadsheetBuilder.Build(data.Columns, data.Rows);
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo generar el XML Spreadsheet: " + ex.Message, "SSMS Quick Tools", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                ClipboardDataObject.SetXmlSpreadsheetAndText(xml, BuildTsvFallback(data));
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo copiar al portapapeles: " + ex.Message, "SSMS Quick Tools", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// TSV simple (encabezados + filas separadas por tab) para destinos que no entienden
        /// "XML Spreadsheet" (Notepad, editores de texto).
        /// </summary>
        private static string BuildTsvFallback(ResultSetData data)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join("\t", data.Columns));
            foreach (var row in data.Rows)
            {
                sb.AppendLine(string.Join("\t", row.Select(v => v ?? "")));
            }
            return sb.ToString();
        }
    }
}
