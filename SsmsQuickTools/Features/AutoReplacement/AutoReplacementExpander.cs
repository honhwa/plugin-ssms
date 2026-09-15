namespace SsmsQuickTools.Features.AutoReplacement
{
    /// <summary>
    /// Texto final a insertar y donde debe quedar el cursor/seleccion tras la expansion.
    /// </summary>
    public sealed class ExpansionResult
    {
        public string Text { get; set; }
        public int CaretOffset { get; set; }
        public bool SelectAll { get; set; }
    }

    /// <summary>
    /// Aplica CursorPositionMarker/SelectReplacement al texto de Replacement. Logica pura.
    /// </summary>
    public static class AutoReplacementExpander
    {
        public static ExpansionResult Build(AutoReplacementEntry entry)
        {
            var replacement = entry.Replacement ?? string.Empty;
            var marker = entry.CursorPositionMarker;

            var text = replacement;
            var caretOffset = replacement.Length;

            if (!string.IsNullOrEmpty(marker))
            {
                var markerChar = marker[0];
                var index = replacement.IndexOf(markerChar);
                if (index >= 0)
                {
                    text = replacement.Substring(0, index) + replacement.Substring(index + 1);
                    caretOffset = index;
                }
            }

            return new ExpansionResult
            {
                Text = text,
                CaretOffset = caretOffset,
                SelectAll = entry.SelectReplacement,
            };
        }
    }
}
