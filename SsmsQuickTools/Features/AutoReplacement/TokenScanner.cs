namespace SsmsQuickTools.Features.AutoReplacement
{
    /// <summary>
    /// Extrae el token pegado al cursor sobre una linea de texto. Logica pura.
    /// </summary>
    public static class TokenScanner
    {
        private static bool IsTokenChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        /// <summary>
        /// Devuelve el token (letras/digitos/'_') inmediatamente a la izquierda del cursor dentro
        /// de <paramref name="linePrefix"/> (el texto de la linea hasta la columna del cursor), y
        /// la columna donde empieza ese token. Null si no hay ningun token pegado al cursor.
        /// </summary>
        public static AutoReplacementToken ExtractTokenBeforeCaret(string linePrefix)
        {
            if (string.IsNullOrEmpty(linePrefix))
            {
                return null;
            }

            var end = linePrefix.Length;
            var start = end;

            while (start > 0 && IsTokenChar(linePrefix[start - 1]))
            {
                start--;
            }

            if (start == end)
            {
                return null;
            }

            return new AutoReplacementToken
            {
                Text = linePrefix.Substring(start, end - start),
                StartColumn = start,
                EndColumn = end,
            };
        }
    }

    /// <summary>
    /// Token encontrado por <see cref="TokenScanner"/>, con su span en la linea de origen.
    /// </summary>
    public sealed class AutoReplacementToken
    {
        public string Text { get; set; }
        public int StartColumn { get; set; }
        public int EndColumn { get; set; }
    }
}
