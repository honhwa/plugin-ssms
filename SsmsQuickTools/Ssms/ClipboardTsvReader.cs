using System;
using System.Windows.Forms;

namespace SsmsQuickTools.Ssms
{
    /// <summary>
    /// Fallback de <see cref="IResultSetReader"/>: el usuario copio el grid con Ctrl+C
    /// (con "Include column headers when copying or saving results" activo en SSMS,
    /// Tools &gt; Options &gt; Query Results &gt; SQL Server &gt; Results to Grid) y este
    /// lector parsea ese TSV desde el portapapeles. Se usa solo si <see cref="GridReader"/> falla.
    /// </summary>
    public sealed class ClipboardTsvReader : IResultSetReader
    {
        public ResultSetData TryRead(out string failureReason)
        {
            string text;
            try
            {
                text = Clipboard.GetText(TextDataFormat.UnicodeText);
            }
            catch (Exception ex)
            {
                failureReason = "No se pudo leer el portapapeles: " + ex.Message;
                return null;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                failureReason = "El portapapeles esta vacio. Copia el resultado del grid (Ctrl+C, con encabezados) e intenta de nuevo.";
                return null;
            }

            var data = TsvParser.Parse(text);
            if (data.Columns.Count == 0)
            {
                failureReason = "El contenido del portapapeles no parece ser un resultado de grid (TSV con encabezados).";
                return null;
            }

            failureReason = null;
            return data;
        }
    }
}
