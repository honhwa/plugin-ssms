using SsmsQuickTools.Features.AutoReplacement;
using Xunit;

namespace SsmsQuickTools.Tests
{
    public class TokenScannerTests
    {
        [Fact]
        public void ExtractTokenBeforeCaret_TokenAlFinalDeLinea_LoDevuelve()
        {
            var token = TokenScanner.ExtractTokenBeforeCaret("cm");
            Assert.NotNull(token);
            Assert.Equal("cm", token.Text);
            Assert.Equal(0, token.StartColumn);
            Assert.Equal(2, token.EndColumn);
        }

        [Fact]
        public void ExtractTokenBeforeCaret_PrecedidoPorEspacio_LoDevuelve()
        {
            var token = TokenScanner.ExtractTokenBeforeCaret("SELECT * FROM x; cm");
            Assert.NotNull(token);
            Assert.Equal("cm", token.Text);
            Assert.Equal(17, token.StartColumn);
        }

        [Fact]
        public void ExtractTokenBeforeCaret_PrecedidoPorParentesis_LoDevuelve()
        {
            var token = TokenScanner.ExtractTokenBeforeCaret("EXEC(cm");
            Assert.NotNull(token);
            Assert.Equal("cm", token.Text);
        }

        [Fact]
        public void ExtractTokenBeforeCaret_PrecedidoPorComa_LoDevuelve()
        {
            var token = TokenScanner.ExtractTokenBeforeCaret("a, cm");
            Assert.NotNull(token);
            Assert.Equal("cm", token.Text);
        }

        [Fact]
        public void ExtractTokenBeforeCaret_LineaVacia_DevuelveNull()
        {
            Assert.Null(TokenScanner.ExtractTokenBeforeCaret(""));
            Assert.Null(TokenScanner.ExtractTokenBeforeCaret(null));
        }

        [Fact]
        public void ExtractTokenBeforeCaret_TerminaEnEspacio_DevuelveNull()
        {
            Assert.Null(TokenScanner.ExtractTokenBeforeCaret("cm "));
        }

        [Fact]
        public void ExtractTokenBeforeCaret_ConDigitosYGuionBajo_LoDevuelve()
        {
            var token = TokenScanner.ExtractTokenBeforeCaret("sel_1");
            Assert.NotNull(token);
            Assert.Equal("sel_1", token.Text);
        }
    }
}
