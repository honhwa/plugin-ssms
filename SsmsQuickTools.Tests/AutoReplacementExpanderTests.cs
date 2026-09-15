using SsmsQuickTools.Features.AutoReplacement;
using Xunit;

namespace SsmsQuickTools.Tests
{
    public class AutoReplacementExpanderTests
    {
        [Fact]
        public void Build_MarcadorPresente_LoBorraYUbicaElCursorAhi()
        {
            var entry = new AutoReplacementEntry
            {
                Replacement = "SELECT # FROM x",
                CursorPositionMarker = "#",
                SelectReplacement = false,
            };

            var result = AutoReplacementExpander.Build(entry);

            Assert.Equal("SELECT  FROM x", result.Text);
            Assert.Equal(7, result.CaretOffset);
            Assert.False(result.SelectAll);
        }

        [Fact]
        public void Build_MarcadorAusente_CursorAlFinal()
        {
            var entry = new AutoReplacementEntry
            {
                Replacement = "EXEC sp_who2 'active'",
                CursorPositionMarker = "$",
                SelectReplacement = false,
            };

            var result = AutoReplacementExpander.Build(entry);

            Assert.Equal("EXEC sp_who2 'active'", result.Text);
            Assert.Equal(result.Text.Length, result.CaretOffset);
        }

        [Fact]
        public void Build_MarcadorVacio_CursorAlFinal()
        {
            var entry = new AutoReplacementEntry
            {
                Replacement = "EXEC sp_who2 'active'",
                CursorPositionMarker = "",
                SelectReplacement = false,
            };

            var result = AutoReplacementExpander.Build(entry);

            Assert.Equal(result.Text.Length, result.CaretOffset);
        }

        [Fact]
        public void Build_SelectReplacementTrue_SelectAllGanaAlMarcador()
        {
            var entry = new AutoReplacementEntry
            {
                Replacement = "SELECT # FROM x",
                CursorPositionMarker = "#",
                SelectReplacement = true,
            };

            var result = AutoReplacementExpander.Build(entry);

            Assert.True(result.SelectAll);
        }

        [Fact]
        public void Build_ReplacementMultilinea_OffsetCaeEnLaLineaCorrecta()
        {
            var entry = new AutoReplacementEntry
            {
                Replacement = "SELECT TOP 200 *\nWHERE 1=1 #\nORDER BY id",
                CursorPositionMarker = "#",
                SelectReplacement = false,
            };

            var result = AutoReplacementExpander.Build(entry);

            var expectedOffset = "SELECT TOP 200 *\nWHERE 1=1 ".Length;
            Assert.Equal(expectedOffset, result.CaretOffset);
            Assert.DoesNotContain("#", result.Text);
        }
    }
}
