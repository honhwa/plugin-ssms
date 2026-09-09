using System.Linq;
using SsmsQuickTools.Ssms;
using Xunit;

namespace SsmsQuickTools.Tests
{
    public class TsvParserTests
    {
        [Theory]
        [InlineData("Id\tNombre\r\n1\tAna\r\n2\tLuis\r\n")]
        [InlineData("Id\tNombre\r1\tAna\r2\tLuis\r")]
        [InlineData("Id\tNombre\n1\tAna\n2\tLuis\n")]
        public void Parse_DistintosSeparadoresDeLinea_ProduceMismoResultado(string tsv)
        {
            var data = Parser(tsv);

            Assert.Equal(new[] { "Id", "Nombre" }, data.Columns);
            Assert.Equal(2, data.Rows.Count);
            Assert.Equal(new[] { "1", "Ana" }, data.Rows[0]);
            Assert.Equal(new[] { "2", "Luis" }, data.Rows[1]);
        }

        [Fact]
        public void Parse_ConCrLfFinalDeCierre_NoGeneraFilaFantasma()
        {
            var data = Parser("Id\tNombre\r\n1\tAna\r\n");

            Assert.Single(data.Rows);
        }

        [Fact]
        public void Parse_FilaConMenosColumnasQueElEncabezado_NoRellenaAqui()
        {
            // TsvParser no rellena; deja la fila corta tal cual, el consumidor
            // (ValuesScriptBuilder.AppendValuesBlock) es quien trata el faltante como NULL.
            var data = Parser("Id\tNombre\tFecha\r\n1\tAna\r\n");

            Assert.Equal(new[] { "1", "Ana" }, data.Rows[0]);
        }

        [Fact]
        public void Parse_CeldaConSaltoDeLineaEmbebido_PartaLaFilaEnDos()
        {
            // Defecto conocido: TsvParser corta por lineas antes de tokenizar columnas, asi
            // que un salto de linea dentro de una celda (nvarchar multilinea del grid) parte
            // la fila. Este test documenta el comportamiento actual, no lo aprueba.
            var data = Parser("Id\tComentario\r\n1\tLinea1\nLinea2\r\n");

            Assert.Equal(2, data.Rows.Count);
            Assert.Equal(new[] { "1", "Linea1" }, data.Rows[0]);
            Assert.Equal(new[] { "Linea2" }, data.Rows[1]);
        }

        [Fact]
        public void Parse_CeldaConTabEmbebido_CorreLasColumnasSiguientes()
        {
            // Defecto conocido: un TAB dentro de una celda desalinea las columnas restantes.
            var data = Parser("Id\tComentario\tExtra\r\n1\tA\tB\tC\r\n");

            Assert.Equal(new[] { "1", "A", "B", "C" }, data.Rows[0]);
        }

        [Fact]
        public void Parse_CeldaVaciaAlFinalDeLaFila_SeConserva()
        {
            var data = Parser("Id\tComentario\r\n1\t\r\n");

            Assert.Equal(new[] { "1", "" }, data.Rows[0]);
        }

        [Fact]
        public void Parse_TextoVacio_DevuelveResultadoVacio()
        {
            var data = Parser("");

            Assert.Empty(data.Columns);
            Assert.Empty(data.Rows);
        }

        private static ResultSetData Parser(string tsv) => TsvParser.Parse(tsv);
    }
}
