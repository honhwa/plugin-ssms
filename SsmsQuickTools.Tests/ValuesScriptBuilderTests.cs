using System.Collections.Generic;
using SsmsQuickTools.Features.ScriptData;
using Xunit;

namespace SsmsQuickTools.Tests
{
    public class ValuesScriptBuilderTests
    {
        [Fact]
        public void Build_TiposMixtos_SinCast()
        {
            var columns = new List<string> { "Id", "Nombre", "Fecha" };
            var rows = new List<string[]>
            {
                new[] { "1", "Ana", "2026-01-15" },
                new[] { "2", "Lu'is", "2026-02-01" },
            };

            var sql = ValuesScriptBuilder.Build(columns, rows);

            Assert.DoesNotContain("CAST(", sql);
            Assert.Contains("(1, N'Ana', '2026-01-15')", sql);
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

            Assert.Contains("(1, NULL)", sql);
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

            Assert.Contains("(1, NULL)", sql);
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
            Assert.DoesNotContain("CAST(", sql);
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

        [Fact]
        public void Build_DecimalConSeparadorDeMiles_GeneraLiteralInvalido()
        {
            // Defecto conocido: decimal.TryParse con NumberStyles.Number acepta "1,234.56",
            // pero FormatLiteral emite el texto crudo (sin quitar la coma), que no es SQL valido.
            var columns = new List<string> { "Monto" };
            var rows = new List<string[]> { new[] { "1,234.56" } };

            var sql = ValuesScriptBuilder.Build(columns, rows);

            Assert.Contains("(1,234.56)", sql); // literal invalido: SQL Server lo leeria como 2 columnas
        }

        [Fact]
        public void Build_CeldaVaciaJuntoAValoresNumericos_DegradaLaColumnaANvarchar()
        {
            // "" no matchea IsNullText (solo null o el texto "NULL") ni ningun TryParse
            // numerico, asi que InferCellType la clasifica NVarChar; al ser el tipo mas
            // especifico de la columna, hasta la fila "5" pasa a citarse como N'5'. No es
            // invalido, pero rompe la expectativa de que quede como entero.
            var columns = new List<string> { "Id", "Cantidad" };
            var rows = new List<string[]>
            {
                new[] { "1", "5" },
                new[] { "2", "" },
            };

            var sql = ValuesScriptBuilder.Build(columns, rows);

            Assert.Contains("(1, N'5')", sql);
            Assert.Contains("(2, N'')", sql);
        }

        [Fact]
        public void Build_TextoLiteralNull_SeConfundeConNullReal()
        {
            // Ambiguedad inherente al TSV del grid: no hay forma de distinguir un nvarchar
            // cuyo VALOR es la cadena "NULL" de una celda realmente NULL.
            var columns = new List<string> { "Comentario" };
            var rows = new List<string[]> { new[] { "NULL" } };

            var sql = ValuesScriptBuilder.Build(columns, rows);

            Assert.Contains("(NULL)", sql);
            Assert.DoesNotContain("N'NULL'", sql);
        }

        [Fact]
        public void Build_DateTimeConMilisegundosYConSieteDecimales_SeFormateaComoTextoLiteral()
        {
            var columns = new List<string> { "ConMs", "ConSieteDecimales" };
            var rows = new List<string[]>
            {
                new[] { "2026-01-15 10:30:00.123", "2026-01-15 10:30:00.1234567" },
            };

            var sql = ValuesScriptBuilder.Build(columns, rows);

            Assert.Contains("'2026-01-15 10:30:00.123'", sql);
            Assert.Contains("'2026-01-15 10:30:00.1234567'", sql);
        }

        [Fact]
        public void Build_ColumnaSoloDeHora_SeInfiereComoDateTime2()
        {
            var columns = new List<string> { "Hora" };
            var rows = new List<string[]> { new[] { "14:30:00" } };

            var types = ValuesScriptBuilder.InferColumnTypes(1, rows);

            Assert.Equal(InferredSqlType.DateTime2, types[0]);
        }

        [Fact]
        public void Build_Varbinary_SeTrataComoTextoNvarchar()
        {
            // Defecto conocido / limitacion aceptada: el grid solo expone texto, asi que un
            // varbinary renderizado como "0x41424344" termina como N'0x...', no como binario.
            var columns = new List<string> { "Datos" };
            var rows = new List<string[]> { new[] { "0x41424344" } };

            var sql = ValuesScriptBuilder.Build(columns, rows);

            Assert.Contains("N'0x41424344'", sql);
        }

        [Fact]
        public void Build_NombresDeColumnaDuplicadosOConEspacios_SeCitanIgual()
        {
            var columns = new List<string> { "Id", "Id", "Nombre Completo", "(No column name)" };
            var rows = new List<string[]> { new[] { "1", "2", "Ana", "x" } };

            var sql = ValuesScriptBuilder.Build(columns, rows);

            // No hay des-duplicacion: la CTE resultante repite [Id], invalida para SQL Server.
            Assert.Contains("[Id], [Id], [Nombre Completo], [(No column name)]", sql);
        }

        [Fact]
        public void Build_MilQuinientasFilasConColumnaNullEnSegundoBloque_CadaBloqueInfiereSuTipo()
        {
            var columns = new List<string> { "Id", "Extra" };
            var rows = new List<string[]>();
            for (var i = 0; i < 1000; i++)
            {
                rows.Add(new[] { i.ToString(), "1" });
            }
            for (var i = 1000; i < 1500; i++)
            {
                rows.Add(new[] { i.ToString(), "NULL" });
            }

            var sql = ValuesScriptBuilder.Build(columns, rows);

            Assert.Contains("UNION ALL", sql);
            Assert.Contains("(1000, NULL)", sql);
        }

        [Fact]
        public void Build_UnicodeYComillasDobles_SeEscapaCorrectamente()
        {
            var columns = new List<string> { "Texto" };
            var rows = new List<string[]> { new[] { "Ñoño \"citado\" áéíóú" } };

            var sql = ValuesScriptBuilder.Build(columns, rows);

            Assert.Contains("N'Ñoño \"citado\" áéíóú'", sql);
        }
    }
}
