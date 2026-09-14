using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SsmsQuickTools.Features.ScriptData;

namespace SsmsQuickTools.Features.CopyXmlSpreadsheet
{
    /// <summary>
    /// Genera un documento XML Spreadsheet 2003 (SpreadsheetML) a partir de un resultado de
    /// grid, preservando tipo y precision: encabezados en negrita, numeros/monetarios como
    /// <c>Number</c> solo si el valor hace round-trip exacto en un double (si no, como texto),
    /// fechas como <c>DateTime</c> (hasta milisegundos), y el resto como <c>String</c>. Logica
    /// pura, sin dependencias de SSMS: reutiliza <see cref="ValuesScriptBuilder.InferColumnTypes"/>
    /// para decidir la categoria de cada columna a partir del texto de las celdas.
    /// </summary>
    public static class XmlSpreadsheetBuilder
    {
        private const string DefaultWorksheetName = "Resultados";

        private enum CellXmlType
        {
            Empty,
            Number,
            DateTime,
            String,
        }

        public static string Build(IReadOnlyList<string> columns, IReadOnlyList<string[]> rows, string worksheetName = DefaultWorksheetName)
        {
            if (columns == null || columns.Count == 0)
            {
                throw new ArgumentException("Se necesita al menos una columna.", nameof(columns));
            }
            if (rows == null)
            {
                throw new ArgumentException("Filas nulas.", nameof(rows));
            }

            var columnCount = columns.Count;
            var rowCount = rows.Count;
            var columnTypes = ValuesScriptBuilder.InferColumnTypes(columnCount, rows);

            var cellTypes = new CellXmlType[rowCount, columnCount];
            var cellValues = new string[rowCount, columnCount];
            var columnScale = new int[columnCount]; // maxima cantidad de decimales vista, por columna numerica
            var columnDateOnly = new bool?[columnCount]; // null: sin celdas DateTime; true: todas sin hora; false: alguna con hora

            for (var c = 0; c < columnCount; c++)
            {
                for (var r = 0; r < rowCount; r++)
                {
                    var row = rows[r];
                    var raw = c < row.Length ? row[c] : null;

                    if (IsNullText(raw))
                    {
                        cellTypes[r, c] = CellXmlType.Empty;
                        continue;
                    }

                    switch (columnTypes[c])
                    {
                        case InferredSqlType.Bit:
                        case InferredSqlType.Int:
                        case InferredSqlType.BigInt:
                        case InferredSqlType.Decimal:
                            if (TryFormatNumericCell(raw, out var formattedNumber, out var decimalPlaces))
                            {
                                cellTypes[r, c] = CellXmlType.Number;
                                cellValues[r, c] = formattedNumber;
                                if (decimalPlaces > columnScale[c])
                                {
                                    columnScale[c] = decimalPlaces;
                                }
                            }
                            else
                            {
                                cellTypes[r, c] = CellXmlType.String;
                                cellValues[r, c] = raw;
                            }
                            break;

                        case InferredSqlType.DateTime2:
                            if (TryFormatDateTimeCell(raw, out var formattedDate, out var isDateOnly))
                            {
                                cellTypes[r, c] = CellXmlType.DateTime;
                                cellValues[r, c] = formattedDate;
                                columnDateOnly[c] = (columnDateOnly[c] ?? true) && isDateOnly;
                            }
                            else
                            {
                                cellTypes[r, c] = CellXmlType.String;
                                cellValues[r, c] = raw;
                            }
                            break;

                        default: // UniqueIdentifier, NVarChar
                            cellTypes[r, c] = CellXmlType.String;
                            cellValues[r, c] = raw;
                            break;
                    }
                }
            }

            var columnStyleIds = new string[columnCount];
            var distinctScales = new SortedSet<int>();
            var usesDateOnly = false;
            var usesDateTime = false;

            for (var c = 0; c < columnCount; c++)
            {
                if (columnDateOnly[c].HasValue)
                {
                    columnStyleIds[c] = columnDateOnly[c].Value ? "sDateOnly" : "sDateTime";
                    if (columnDateOnly[c].Value)
                    {
                        usesDateOnly = true;
                    }
                    else
                    {
                        usesDateTime = true;
                    }
                }
                else if (columnScale[c] > 0)
                {
                    columnStyleIds[c] = "sDecimal" + columnScale[c].ToString(CultureInfo.InvariantCulture);
                    distinctScales.Add(columnScale[c]);
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\"?>");
            sb.AppendLine("<?mso-application progid=\"Excel.Sheet\"?>");
            sb.AppendLine("<Workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\" xmlns:ss=\"urn:schemas-microsoft-com:office:spreadsheet\">");
            sb.AppendLine("  <Styles>");
            sb.AppendLine("    <Style ss:ID=\"sHeader\"><Font ss:Bold=\"1\"/></Style>");
            if (usesDateOnly)
            {
                sb.AppendLine("    <Style ss:ID=\"sDateOnly\"><NumberFormat ss:Format=\"yyyy\\-mm\\-dd\"/></Style>");
            }
            if (usesDateTime)
            {
                sb.AppendLine("    <Style ss:ID=\"sDateTime\"><NumberFormat ss:Format=\"yyyy\\-mm\\-dd hh:mm:ss\"/></Style>");
            }
            foreach (var scale in distinctScales)
            {
                var format = "0." + new string('0', scale);
                sb.Append("    <Style ss:ID=\"sDecimal").Append(scale.ToString(CultureInfo.InvariantCulture))
                  .Append("\"><NumberFormat ss:Format=\"").Append(format).AppendLine("\"/></Style>");
            }
            sb.AppendLine("  </Styles>");

            sb.Append("  <Worksheet ss:Name=\"").Append(XmlEscape(SanitizeWorksheetName(worksheetName))).AppendLine("\">");
            sb.AppendLine("    <Table>");

            for (var c = 0; c < columnCount; c++)
            {
                if (columnStyleIds[c] != null)
                {
                    sb.Append("      <Column ss:Index=\"").Append((c + 1).ToString(CultureInfo.InvariantCulture))
                      .Append("\" ss:StyleID=\"").Append(columnStyleIds[c]).AppendLine("\"/>");
                }
            }

            sb.AppendLine("      <Row ss:StyleID=\"sHeader\">");
            foreach (var column in columns)
            {
                sb.Append("        <Cell><Data ss:Type=\"String\">").Append(XmlEscape(column)).AppendLine("</Data></Cell>");
            }
            sb.AppendLine("      </Row>");

            for (var r = 0; r < rowCount; r++)
            {
                sb.AppendLine("      <Row>");
                for (var c = 0; c < columnCount; c++)
                {
                    var type = cellTypes[r, c];
                    if (type == CellXmlType.Empty)
                    {
                        sb.AppendLine("        <Cell/>");
                        continue;
                    }

                    var typeAttr = type == CellXmlType.Number ? "Number" : type == CellXmlType.DateTime ? "DateTime" : "String";
                    sb.Append("        <Cell><Data ss:Type=\"").Append(typeAttr).Append("\">")
                      .Append(XmlEscape(cellValues[r, c])).AppendLine("</Data></Cell>");
                }
                sb.AppendLine("      </Row>");
            }

            sb.AppendLine("    </Table>");
            sb.AppendLine("  </Worksheet>");
            sb.Append("</Workbook>");

            return sb.ToString();
        }

        /// <summary>
        /// Intenta clasificar una celda numerica (columna Bit/Int/BigInt/Decimal) como
        /// <c>Number</c> exacto: sin separador de miles, sin ceros a la izquierda espurios, y
        /// dentro de los ~15 digitos significativos que un double IEEE-754 representa sin
        /// perdida. Si algo de esto falla, la celda debe caer a <c>String</c> (ver M4 en
        /// docs/PLAN.md, "Reglas de exactitud").
        /// </summary>
        private static bool TryFormatNumericCell(string raw, out string formatted, out int decimalPlaces)
        {
            formatted = null;
            decimalPlaces = 0;

            var trimmed = raw.Trim();
            if (trimmed.Length == 0 || trimmed.IndexOf(',') >= 0)
            {
                return false; // separador de miles: ValuesScriptBuilder lo acepta, nosotros no
            }

            var body = trimmed;
            if (body.Length > 0 && (body[0] == '-' || body[0] == '+'))
            {
                body = body.Substring(1);
            }

            var dotIndex = body.IndexOf('.');
            var intPart = dotIndex >= 0 ? body.Substring(0, dotIndex) : body;
            var fracPart = dotIndex >= 0 ? body.Substring(dotIndex + 1) : "";

            if (intPart.Length == 0 || !intPart.All(char.IsDigit) || (fracPart.Length > 0 && !fracPart.All(char.IsDigit)))
            {
                return false;
            }

            if (intPart.Length > 1 && intPart[0] == '0')
            {
                return false; // ceros a la izquierda: "00123" no es un numero, es un codigo
            }

            if (dotIndex < 0)
            {
                if (!long.TryParse(trimmed, NumberStyles.Integer | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var asLong))
                {
                    return false; // no entra en long: bigint mas grande que eso, mucho menos en un double
                }
                if (asLong < -9007199254740991L || asLong > 9007199254740991L) // -(2^53-1) .. 2^53-1
                {
                    return false;
                }

                formatted = trimmed;
                decimalPlaces = 0;
                return true;
            }

            if (!decimal.TryParse(trimmed, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
            {
                return false;
            }

            var significantIntDigits = intPart.TrimStart('0');
            if (significantIntDigits.Length == 0)
            {
                significantIntDigits = "0";
            }
            var significantDigitCount = significantIntDigits.Length + fracPart.Length;
            if (significantDigitCount > 15)
            {
                return false;
            }

            formatted = trimmed;
            decimalPlaces = fracPart.Length;
            return true;
        }

        private static readonly Regex FractionSecondsPattern = new Regex(@"\.(\d+)", RegexOptions.Compiled);

        /// <summary>
        /// Intenta clasificar una celda de columna DateTime2 como <c>DateTime</c> de
        /// SpreadsheetML: hasta 3 digitos de fraccion de segundo, fecha desde 1900-01-01, y sin
        /// ser un valor solo-hora (SpreadsheetML no tiene un tipo "time" limpio). Si algo de
        /// esto falla, la celda cae a <c>String</c> con el texto exacto.
        /// </summary>
        private static bool TryFormatDateTimeCell(string raw, out string formatted, out bool isDateOnly)
        {
            formatted = null;
            isDateOnly = false;

            // Valor solo-hora ("10:20:30", "10:20:30.1234567"): sin separador de fecha, se
            // descarta -> String, para no inventarle una fecha arbitraria.
            if (raw.IndexOf(':') >= 0 && raw.IndexOfAny(new[] { '-', '/' }) < 0)
            {
                return false;
            }

            if (!System.DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return false;
            }

            if (parsed.Year < 1900)
            {
                return false; // fuera del sistema de fecha serial de Excel
            }

            var fractionDigits = 0;
            var match = FractionSecondsPattern.Match(raw);
            if (match.Success)
            {
                fractionDigits = match.Groups[1].Value.Length;
                if (fractionDigits > 3)
                {
                    return false; // datetime2(4..7): truncar en silencio no es aceptable
                }
            }

            isDateOnly = raw.IndexOf(':') < 0;

            var baseText = parsed.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
            formatted = fractionDigits > 0
                ? baseText + "." + parsed.ToString("fff", CultureInfo.InvariantCulture).Substring(0, fractionDigits)
                : baseText;
            return true;
        }

        private static bool IsNullText(string value) => value == null || value == ValuesScriptBuilder.NullLiteral;

        private static string SanitizeWorksheetName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return DefaultWorksheetName;
            }

            var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
            var cleaned = new string(name.Where(ch => Array.IndexOf(invalid, ch) < 0).ToArray());
            if (cleaned.Length == 0)
            {
                cleaned = DefaultWorksheetName;
            }
            return cleaned.Length > 31 ? cleaned.Substring(0, 31) : cleaned;
        }

        /// <summary>
        /// Escapa entidades XML y elimina caracteres de control ilegales en XML 1.0
        /// (se preservan tab/CR/LF).
        /// </summary>
        private static string XmlEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }

            var sb = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                if (char.IsControl(ch) && ch != '\t' && ch != '\n' && ch != '\r')
                {
                    continue;
                }
                switch (ch)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    default: sb.Append(ch); break;
                }
            }
            return sb.ToString();
        }
    }
}
