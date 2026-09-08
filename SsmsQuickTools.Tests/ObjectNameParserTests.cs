using SsmsQuickTools.Features.ScriptObject;
using Xunit;

namespace SsmsQuickTools.Tests
{
    public class ObjectNameParserTests
    {
        [Fact]
        public void Parse_NombreSimple_UsaDboPorDefecto()
        {
            var result = ObjectNameParser.Parse("Clientes");
            Assert.Equal("dbo", result.Schema);
            Assert.Equal("Clientes", result.Name);
            Assert.Null(result.Database);
        }

        [Fact]
        public void Parse_SchemaPuntoObjeto()
        {
            var result = ObjectNameParser.Parse("ventas.Clientes");
            Assert.Equal("ventas", result.Schema);
            Assert.Equal("Clientes", result.Name);
        }

        [Fact]
        public void Parse_TresPartesConCorchetes()
        {
            var result = ObjectNameParser.Parse("[MiDb].[dbo].[Clientes]");
            Assert.Equal("MiDb", result.Database);
            Assert.Equal("dbo", result.Schema);
            Assert.Equal("Clientes", result.Name);
        }

        [Fact]
        public void Parse_CorchetesMixtosConPuntoDentroDeCorchetes()
        {
            var result = ObjectNameParser.Parse("dbo.[Mi.Tabla]");
            Assert.Equal("dbo", result.Schema);
            Assert.Equal("Mi.Tabla", result.Name);
        }

        [Fact]
        public void Parse_ConPuntoYComaAlFinal_LoIgnora()
        {
            var result = ObjectNameParser.Parse("dbo.Clientes;");
            Assert.Equal("Clientes", result.Name);
        }

        [Fact]
        public void Parse_Vacio_DevuelveNull()
        {
            Assert.Null(ObjectNameParser.Parse(""));
            Assert.Null(ObjectNameParser.Parse("   "));
            Assert.Null(ObjectNameParser.Parse(null));
        }

        [Fact]
        public void Parse_DemasiadasPartes_DevuelveNull()
        {
            Assert.Null(ObjectNameParser.Parse("a.b.c.d"));
        }

        [Fact]
        public void QuotedSchemaQualifiedName_EscapaCorchetes()
        {
            // "Raro]]Nombre" entre corchetes representa el nombre real "Raro]Nombre"
            // (corchete literal escapado como "]]"); al recomponer, se vuelve a doblar igual.
            var result = ObjectNameParser.Parse("dbo.[Raro]]Nombre]");
            Assert.Equal("Raro]Nombre", result.Name);
            Assert.Equal("[dbo].[Raro]]Nombre]", result.QuotedSchemaQualifiedName);
        }
    }

    public class CreateAlterRewriterTests
    {
        [Fact]
        public void ReplaceCreateWithAlter_CasoSimple()
        {
            var sql = "CREATE PROCEDURE dbo.Foo AS SELECT 1";
            var result = CreateAlterRewriter.ReplaceCreateWithAlter(sql);
            Assert.StartsWith("ALTER PROCEDURE", result);
        }

        [Fact]
        public void ReplaceCreateWithAlter_RespetaComentarioPrevio()
        {
            var sql = "-- comentario\nCREATE VIEW dbo.V AS SELECT 1";
            var result = CreateAlterRewriter.ReplaceCreateWithAlter(sql);
            Assert.Equal("-- comentario\nALTER VIEW dbo.V AS SELECT 1", result);
        }

        [Fact]
        public void ReplaceCreateWithAlter_SinCreate_DevuelveIgual()
        {
            var sql = "SELECT 1";
            Assert.Equal(sql, CreateAlterRewriter.ReplaceCreateWithAlter(sql));
        }
    }
}
