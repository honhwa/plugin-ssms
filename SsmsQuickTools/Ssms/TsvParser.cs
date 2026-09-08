using System;
using System.Collections.Generic;

namespace SsmsQuickTools.Ssms
{
    /// <summary>
    /// Parsea texto separado por tabs como el que produce el grid de resultados de SSMS,
    /// tanto via <see cref="Grid.IGridControl.GetDataObject"/> como via Ctrl+C manual.
    /// Se usa desde <see cref="GridReader"/> y <see cref="ClipboardTsvReader"/>.
    /// </summary>
    public static class TsvParser
    {
        /// <summary>
        /// Parsea TSV con la primera fila como encabezados. Acepta separadores de linea
        /// \r\n, \r o \n. Filas vacias al final se ignoran.
        /// </summary>
        public static ResultSetData Parse(string tsv)
        {
            if (string.IsNullOrEmpty(tsv))
            {
                return new ResultSetData(Array.Empty<string>(), Array.Empty<string[]>());
            }

            var lines = tsv.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

            var nonEmptyLines = new List<string>();
            foreach (var line in lines)
            {
                if (nonEmptyLines.Count == 0 && line.Length == 0)
                {
                    continue; // ignora lineas en blanco iniciales
                }
                nonEmptyLines.Add(line);
            }

            // recorta lineas vacias al final (SSMS suele dejar un \r\n de cierre)
            while (nonEmptyLines.Count > 0 && nonEmptyLines[nonEmptyLines.Count - 1].Length == 0)
            {
                nonEmptyLines.RemoveAt(nonEmptyLines.Count - 1);
            }

            if (nonEmptyLines.Count == 0)
            {
                return new ResultSetData(Array.Empty<string>(), Array.Empty<string[]>());
            }

            var columns = nonEmptyLines[0].Split('\t');
            var rows = new List<string[]>(nonEmptyLines.Count - 1);
            for (var i = 1; i < nonEmptyLines.Count; i++)
            {
                rows.Add(nonEmptyLines[i].Split('\t'));
            }

            return new ResultSetData(columns, rows);
        }
    }
}
