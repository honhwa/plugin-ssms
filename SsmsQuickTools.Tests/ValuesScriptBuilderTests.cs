using System.Collections.Generic;
using SsmsQuickTools.Features.ScriptData;
using Xunit;

namespace SsmsQuickTools.Tests
{
    public class ValuesScriptBuilderTests
    {
        [Fact]
        public void Build_TiposMixtos_GeneraCastEnPrimeraFila()
        {
            var columns = new List<string> { "Id", "Nombre", "Fecha" };
            var rows = new List<string[]>
            {
                new[] { "1", "Ana", "2026-01-15" },
                new[] { "2", "Lu'is", "2026-02-01" },
            };

            var sql = ValuesScriptBuilder.Build(columns, rows);

            Assert.Contains("CAST(1 AS int)", sql);
            Assert.Contains("CAST(N'Ana' AS nvarchar(max))", sql);
            Assert.Contains("CAST('2026-01-15' AS datetime2(3))", sql);
            Assert.Contains("N'Lu''is'", sql); // escapado de comilla simple
            Assert.Contains("WITH [datos]", sql);
            Assert.Contains("SELECT * FROM [datos]", sql);
        }

        [Fact]
        public void Build_ValorNull_SeConvierteALiteralNull()
        {
            var columns = new List<string> { "Id", "Comentario" };
            var rows = new List<string[]>
            {
                new[] { "1", "NULL" },
                new[] { "2", "algo" },
            };

            var sql = ValuesScriptBuilder.Build(columns, rows);

            Assert.Contains("CAST(NULL AS nvarchar(max))", sql);
            Assert.Contains("N'algo'", sql);
        }

        [Fact]
        public void Build_ColumnaEnteramenteNull_UsaNvarcharPorDefecto()
        {
            var columns = new List<string> { "Id", "Comentario" };
            var rows = new List<string[]>
            {
                new[] { "1", "NULL" },
                new[] { "2", "NULL" },
            };

            var sql = ValuesScriptBuilder.Build(columns, rows);

            Assert.Contains("CAST(NULL AS nvarchar(max))", sql);
        }

        [Fact]
        public void Build_SinFilas_GeneraSelectVacio()
        {
            var columns = new List<string> { "Id" };
            var rows = new List<string[]>();

            var sql = ValuesScriptBuilder.Build(columns, rows);

            Assert.Contains("WHERE 1 = 0", sql);
        }

        [Fact]
        public void Build_MasDe1000Filas_ParticionaEnBloquesConUnionAll()
        {
            var columns = new List<string> { "Id" };
            var rows = new List<string[]>();
            for (var i = 0; i < 1500; i++)
            {
                rows.Add(new[] { i.ToString() });
            }

            var sql = ValuesScriptBuilder.Build(columns, rows);

            Assert.Contains("UNION ALL", sql);
            // cada bloque castea su propia primera fila
            var castCount = System.Text.RegularExpressions.Regex.Matches(sql, "CAST\\(").Count;
            Assert.Equal(2, castCount);
        }

        [Theory]
        [InlineData("bit")]
        public void InferColumnTypes_SoloUnosYCeros_InfiereBit(string _)
        {
            var rows = new List<string[]> { new[] { "1" }, new[] { "0" }, new[] { "1" } };
            var types = ValuesScriptBuilder.InferColumnTypes(1, rows);
            Assert.Equal(InferredSqlType.Bit, types[0]);
        }

        [Fact]
        public void InferColumnTypes_GuidValido_InfiereUniqueIdentifier()
        {
            var rows = new List<string[]> { new[] { System.Guid.NewGuid().ToString() } };
            var types = ValuesScriptBuilder.InferColumnTypes(1, rows);
            Assert.Equal(InferredSqlType.UniqueIdentifier, types[0]);
        }

        [Fact]
        public void QuoteIdentifier_EscapaCorcheteDeCierre()
        {
            Assert.Equal("[a]]b]", ValuesScriptBuilder.QuoteIdentifier("a]b"));
        }
    }
}
