using System.Collections.Generic;

namespace SsmsQuickTools.Features.AutoReplacement
{
    /// <summary>
    /// Una entrada de autoreplacement.xml: uno o varios tokens (alias) que expanden al mismo
    /// texto de reemplazo. Logica pura, sin dependencias de SSMS/VS.
    /// </summary>
    public sealed class AutoReplacementEntry
    {
        public List<string> Tokens { get; set; } = new List<string>();
        public bool CaseSensitive { get; set; }
        public string Name { get; set; }
        public string Replacement { get; set; }
        public bool SelectReplacement { get; set; }
        public string CursorPositionMarker { get; set; }
    }
}
