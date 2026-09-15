using SsmsQuickTools.Features.AutoReplacement;
using Xunit;

namespace SsmsQuickTools.Tests
{
    public class SqlContextScannerTests
    {
        [Fact]
        public void IsInsideLiteralOrComment_EnCodigo_DevuelveFalse()
        {
            Assert.False(SqlContextScanner.IsInsideLiteralOrComment("SELECT * FROM x WHERE 1=1 cm"));
        }

        [Fact]
        public void IsInsideLiteralOrComment_DentroDeCadena_DevuelveTrue()
        {
            Assert.True(SqlContextScanner.IsInsideLiteralOrComment("SELECT 'cm"));
        }

        [Fact]
        public void IsInsideLiteralOrComment_DespuesDeComillaEscapada_DevuelveFalse()
        {
            Assert.False(SqlContextScanner.IsInsideLiteralOrComment("SELECT 'it''s' cm"));
        }

        [Fact]
        public void IsInsideLiteralOrComment_DentroDeComentarioDeLinea_DevuelveTrue()
        {
            Assert.True(SqlContextScanner.IsInsideLiteralOrComment("SELECT 1 -- cm"));
        }

        [Fact]
        public void IsInsideLiteralOrComment_EnLineaSiguienteAComentario_DevuelveFalse()
        {
            Assert.False(SqlContextScanner.IsInsideLiteralOrComment("SELECT 1 -- comentario\ncm"));
        }

        [Fact]
        public void IsInsideLiteralOrComment_DentroDeComentarioDeBloque_DevuelveTrue()
        {
            Assert.True(SqlContextScanner.IsInsideLiteralOrComment("SELECT 1 /* cm"));
        }

        [Fact]
        public void IsInsideLiteralOrComment_DentroDeBloqueAnidado_DevuelveTrue()
        {
            Assert.True(SqlContextScanner.IsInsideLiteralOrComment("/* externo /* interno */ cm"));
        }

        [Fact]
        public void IsInsideLiteralOrComment_DespuesDeCerrarBloqueAnidado_DevuelveFalse()
        {
            Assert.False(SqlContextScanner.IsInsideLiteralOrComment("/* externo /* interno */ */ cm"));
        }

        [Fact]
        public void IsInsideLiteralOrComment_DentroDeCorchetes_DevuelveTrue()
        {
            Assert.True(SqlContextScanner.IsInsideLiteralOrComment("SELECT * FROM [cm"));
        }
    }
}
