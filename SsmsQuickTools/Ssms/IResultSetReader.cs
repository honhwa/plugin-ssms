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

        public ResultSetData(IReadOnlyList<string> columns, IReadOnlyList<string[]> rows)
        {
            Columns = columns;
            Rows = rows;
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
