using System.Collections.Generic;

namespace SsmsQuickTools.Ssms
{
    /// <summary>
    /// Un resultado leido del grid de SSMS: nombres de columna y filas como texto tal como
    /// las muestra el grid (sin tipos SQL - eso se infiere despues en ValuesScriptBuilder).
    /// </summary>
    public sealed class ResultSetData
    {
        public IReadOnlyList<string> Columns { get; }
        public IReadOnlyList<string[]> Rows { get; }

        /// <summary>
        /// true si estos datos vienen de una seleccion explicita de celdas en el grid (en vez
        /// del grid completo). <see cref="ClipboardTsvReader"/> nunca puede saberlo con
        /// certeza y siempre reporta false. Usado por comandos que requieren seleccion
        /// explicita, como "Copiar seleccion como XML Spreadsheet".
        /// </summary>
        public bool HasSelection { get; }

        public ResultSetData(IReadOnlyList<string> columns, IReadOnlyList<string[]> rows, bool hasSelection = false)
        {
            Columns = columns;
            Rows = rows;
            HasSelection = hasSelection;
        }
    }

    /// <summary>
    /// Fuente de datos para "Copiar resultado como script SELECT". Hay dos implementaciones:
    /// <see cref="GridReader"/> (principal, lee el grid activo directamente) y
    /// <see cref="ClipboardTsvReader"/> (fallback manual, si el usuario copio el grid con Ctrl+C).
    /// </summary>
    public interface IResultSetReader
    {
        /// <summary>
        /// Intenta leer el resultado. Devuelve null si esta fuente no pudo obtener datos
        /// (nunca lanza para errores esperables, para poder encadenar con un fallback).
        /// </summary>
        ResultSetData TryRead(out string failureReason);
    }
}
