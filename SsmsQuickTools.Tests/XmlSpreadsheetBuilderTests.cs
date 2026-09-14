using System.Collections.Generic;
using SsmsQuickTools.Features.CopyXmlSpreadsheet;
using Xunit;

namespace SsmsQuickTools.Tests
{
    public class XmlSpreadsheetBuilderTests
    {
        [Fact]
        public void Build_Encabezados_UsanEstiloNegrita()
        {
            var columns = new List<string> { "Id", "Nombre" };
            var rows = new List<string[]> { new[] { "1", "Ana" } };

            var xml = XmlSpreadsheetBuilder.Build(columns, rows);

            Assert.Contains("<Style ss:ID=\"sHeader\"><Font ss:Bold=\"1\"/></Style>", xml);
            Assert.Contains("<Row ss:StyleID=\"sHeader\">", xml);
            Assert.Contains("<Data ss:Type=\"String\">Id</Data>", xml);
        }

        [Fact]
        public void Build_TextoConComillaSimple_QuedaComoString()
        {
            var columns = new List<string> { "Nombre" };
            var rows = new List<string[]> { new[] { "O'Brien" } };

            var xml = XmlSpreadsheetBuilder.Build(columns, rows);

            Assert.Contains("<Data ss:Type=\"String\">O'Brien</Data>", xml);
        }

        [Fact]
        public void Build_EnteroSimple_EsNumber()
        {
            var columns = new List<string> { "Id" };
            var rows = new List<string[]> { new[] { "123" } };

            var xml = XmlSpreadsheetBuilder.Build(columns, rows);

            Assert.Contains("<Data ss:Type=\"Number\">123</Data>", xml);
        }

        [Fact]
        public void Build_BigIntMayorA2Elevado53_CaeAString()
        {
            var columns = new List<string> { "Id" };
            var rows = new List<string[]> { new[] { "9007199254740993" } }; // 2^53 + 2

            var xml = XmlSpreadsheetBuilder.Build(columns, rows);

            Assert.Contains("<Data ss:Type=\"String\">9007199254740993</Data>", xml);
        }

        [Fact]
        public void Build_DecimalCon15DigitosSignificativos_EsNumberConEscalaPreservada()
        {
            var columns = new List<string> { "Importe" };
            var rows = new List<string[]> { new[] { "12.50" } };

            var xml = XmlSpreadsheetBuilder.Build(columns, rows);

            Assert.Contains("<Data ss:Type=\"Number\">12.50</Data>", xml);
            Assert.Contains("<Style ss:ID=\"sDecimal2\"><NumberFormat ss:Format=\"0.00\"/></Style>", xml);
            Assert.Contains("ss:StyleID=\"sDecimal2\"", xml);
        }

        [Fact]
        public void Build_DecimalConMasDe15DigitosSignificativos_CaeAString()
        {
            var columns = new List<string> { "Importe" };
            var rows = new List<string[]> { new[] { "1234567890123.456" } }; // 16 digitos significativos

            var xml = XmlSpreadsheetBuilder.Build(columns, rows);

            Assert.Contains("<Data ss:Type=\"String\">1234567890123.456</Data>", xml);
        }

        [Fact]
        public void Build_NumeroConSeparadorDeMiles_CaeAString()
        {
            var columns = new List<string> { "Importe" };
            var rows = new List<string[]> { new[] { "1,234.50" } };

            var xml = XmlSpreadsheetBuilder.Build(columns, rows);

            Assert.Contains("<Data ss:Type=\"String\">1,234.50</Data>", xml);
        }

        [Fact]
        public void Build_CerosALaIzquierda_CaeAString()
        {
            var columns = new List<string> { "Documento" };
            var rows = new List<string[]> { new[] { "00123" } };

            var xml = XmlSpreadsheetBuilder.Build(columns, rows);

            Assert.Contains("<Data ss:Type=\"String\">00123</Data>", xml);
        }

        [Fact]
        public void Build_ValorNull_GeneraCeldaVacia()
        {
            var columns = new List<string> { "Id", "Comentario" };
            var rows = new List<string[]> { new[] { "1", "NULL" } };

            var xml = XmlSpreadsheetBuilder.Build(columns, rows);

            Assert.Contains("<Cell/>", xml);
            Assert.DoesNotContain(">NULL<", xml);
        }

        [Fact]
        public void Build_FechaSinHora_EsDateTimeConEstiloSoloFecha()
        {
            var columns = new List<string> { "Fecha" };
            var rows = new List<string[]> { new[] { "2026-03-04" } };

            var xml = XmlSpreadsheetBuilder.Build(columns, rows);

            Assert.Contains("<Data ss:Type=\"DateTime\">2026-03-04T00:00:00</Data>", xml);
            Assert.Contains("<Style ss:ID=\"sDateOnly\">", xml);
            Assert.Contains("ss:StyleID=\"sDateOnly\"", xml);
        }

        [Fact]
        public void Build_FechaConHora_EsDateTimeConEstiloCompleto()
        {
            var columns = new List<string> { "Fecha" };
            var rows = new List<string[]> { new[] { "2026-03-04 10:20:30" } };

            var xml = XmlSpreadsheetBuilder.Build(columns, rows);

            Assert.Contains("<Data ss:Type=\"DateTime\">2026-03-04T10:20:30</Data>", xml);
            Assert.Contains("<Style ss:ID=\"sDateTime\">", xml);
        }

        [Fact]
        public void Build_FechaConMasDe3DigitosDeFraccion_CaeAString()
        {
            var columns = new List<string> { "Fecha" };
            var rows = new List<string[]> { new[] { "2026-03-04 10:20:30.1234567" } };

            var xml = XmlSpreadsheetBuilder.Build(columns, rows);

            Assert.Contains("<Data ss:Type=\"String\">2026-03-04 10:20:30.1234567</Data>", xml);
        }

        [Fact]
        public void Build_ValorSoloHora_CaeAString()
        {
            var columns = new List<string> { "Hora" };
            var rows = new List<string[]> { new[] { "10:20:30" } };

            var xml = XmlSpreadsheetBuilder.Build(columns, rows);

            Assert.Contains("<Data ss:Type=\"String\">10:20:30</Data>", xml);
        }

        [Fact]
        public void Build_FechaAnteriorA1900_CaeAString()
        {
            var columns = new List<string> { "Fecha" };
            var rows = new List<string[]> { new[] { "1850-01-01" } };

            var xml = XmlSpreadsheetBuilder.Build(columns, rows);

            Assert.Contains("<Data ss:Type=\"String\">1850-01-01</Data>", xml);
        }

        [Fact]
        public void Build_Uniqueidentifier_EsString()
        {
            var columns = new List<string> { "Id" };
            var rows = new List<string[]> { new[] { "3F2504E0-4F89-11D3-9A0C-0305E82C3301" } };

            var xml = XmlSpreadsheetBuilder.Build(columns, rows);

            Assert.Contains("<Data ss:Type=\"String\">3F2504E0-4F89-11D3-9A0C-0305E82C3301</Data>", xml);
        }

        [Fact]
        public void Build_TextoConCaracteresEspeciales_SeEscapa()
        {
            var columns = new List<string> { "Nota" };
            var rows = new List<string[]> { new[] { "a < b & c > d" } };

            var xml = XmlSpreadsheetBuilder.Build(columns, rows);

            Assert.Contains("<Data ss:Type=\"String\">a &lt; b &amp; c &gt; d</Data>", xml);
        }

        [Fact]
        public void Build_ColumnaBit_EsNumber()
        {
            var columns = new List<string> { "Activo" };
            var rows = new List<string[]> { new[] { "1" }, new[] { "0" } };

            var xml = XmlSpreadsheetBuilder.Build(columns, rows);

            Assert.Contains("<Data ss:Type=\"Number\">1</Data>", xml);
            Assert.Contains("<Data ss:Type=\"Number\">0</Data>", xml);
            Assert.DoesNotContain("ss:Type=\"Boolean\"", xml);
        }
    }
}
