using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.SqlClient;
using SsmsQuickTools.Features.ScriptData;
using SsmsQuickTools.Ssms;
using Xunit;

namespace SsmsQuickTools.Tests
{
    /// <summary>
    /// Round-trip real contra el motor: TSV de fixture -> TsvParser -> ValuesScriptBuilder ->
    /// ejecutar el script generado -> comparar filas devueltas contra el fixture. Cierra el
    /// hueco que los unit tests de <see cref="ValuesScriptBuilderTests"/> no cubren: que el SQL
    /// generado de verdad EJECUTE, no solo que el texto tenga la forma esperada.
    ///
    /// El script real produce <c>INSERT INTO XXXXXXXX ...</c>, que no ejecuta solo (el marcador
    /// no es una tabla real). Para poder correr el round-trip se quita el prefijo <c>INSERT INTO
    /// XXXXXXXX</c> con <see cref="ToSelectOnly"/> y se ejecuta el <c>SELECT * FROM (VALUES ...)</c>
    /// suelto, que sí es válido por sí mismo.
    ///
    /// Se salta entero si SSMSQT_TEST_CONNECTION no esta definida, para que `dotnet test` siga
    /// verde en una maquina sin servidor. En la maquina de desarrollo:
    ///   SSMSQT_TEST_CONNECTION=Server=LENOVOJOSE\DEV01;Database=Figuritas;Integrated Security=true;TrustServerCertificate=true
    ///
    /// Los .tsv en Fixtures/ estan escritos a mano imitando el formato que produce el grid de
    /// SSMS; lo ideal (Capa 3 del plan) es reemplazarlos por TSV capturado de verdad con Ctrl+C
    /// en SSMS, que es la unica forma de confirmar el formato exacto de fecha/decimal/GUID que
    /// usa el grid en la practica.
    /// </summary>
    public class ScriptRoundTripTests
    {
        private static string ConnectionString =>
            Environment.GetEnvironmentVariable("SSMSQT_TEST_CONNECTION");

        private static bool HasConnection => !string.IsNullOrWhiteSpace(ConnectionString);

        /// <summary>
        /// Quita el prefijo <c>INSERT INTO XXXXXXXX</c> de cada statement, dejando los
        /// <c>SELECT * FROM (VALUES ...) v (...)</c> sueltos y ejecutables por separado.
        /// </summary>
        private static string ToSelectOnly(string sql) =>
            sql.Replace("INSERT INTO " + ValuesScriptBuilder.DefaultTargetTable + Environment.NewLine, string.Empty);

        [SkippableFact]
        public void TiposMixtos_ScriptGeneradoEjecutaYDevuelveLasMismasFilas()
        {
            Skip.IfNot(HasConnection, "SSMSQT_TEST_CONNECTION no esta definida.");

            var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "tipos_mixtos.tsv");
            var tsv = File.ReadAllText(fixturePath);
            var data = TsvParser.Parse(tsv);

            var sql = ValuesScriptBuilder.Build(data.Columns.ToList(), data.Rows.ToList());

            using (var connection = new SqlConnection(ConnectionString))
            {
                connection.Open();
                using (var command = new SqlCommand(ToSelectOnly(sql), connection))
                using (var reader = command.ExecuteReader())
                {
                    var actualRows = new List<string[]>();
                    while (reader.Read())
                    {
                        var row = new string[reader.FieldCount];
                        for (var c = 0; c < reader.FieldCount; c++)
                        {
                            row[c] = reader.IsDBNull(c) ? null : NormalizeCell(reader.GetValue(c));
                        }
                        actualRows.Add(row);
                    }

                    Assert.Equal(data.Rows.Count, actualRows.Count);
                    for (var r = 0; r < data.Rows.Count; r++)
                    {
                        for (var c = 0; c < data.Columns.Count; c++)
                        {
                            var expected = NormalizeExpected(c < data.Rows[r].Length ? data.Rows[r][c] : null);
                            Assert.Equal(expected, actualRows[r][c]);
                        }
                    }
                }
            }
        }

        [SkippableFact]
        public void SeleccionParcial_ScriptGeneradoEjecutaYDevuelveLaFilaSeleccionada()
        {
            // Fixture capturado en SSMS real (checklist Capa 3, paso 2): seleccionar un
            // subconjunto de filas del grid de tipos_mixtos y copiar. Solo cubre seleccion
            // parcial de FILAS -- la captura recibida trae las 7 columnas completas, asi que
            // seleccion parcial de COLUMNAS sigue sin evidencia real.
            Skip.IfNot(HasConnection, "SSMSQT_TEST_CONNECTION no esta definida.");

            var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "tipos_mixtos_seleccion_parcial.tsv");
            var data = TsvParser.Parse(File.ReadAllText(fixturePath));
            Assert.Single(data.Rows); // confirma que la captura es en efecto un subconjunto (1 de 2 filas)

            var sql = ValuesScriptBuilder.Build(data.Columns.ToList(), data.Rows.ToList());

            using (var connection = new SqlConnection(ConnectionString))
            {
                connection.Open();
                using (var command = new SqlCommand(ToSelectOnly(sql), connection))
                using (var reader = command.ExecuteReader())
                {
                    Assert.True(reader.Read());
                    Assert.Equal("1", NormalizeCell(reader.GetValue(0)));
                    Assert.Equal("O'Brien", NormalizeCell(reader.GetValue(1)));
                    Assert.False(reader.Read());
                }
            }
        }

        [SkippableFact]
        public void Volumen1500FilasReales_DoceColumnasSysObjects_EjecutaYDevuelveMismaCantidad()
        {
            // Fixture capturado en SSMS real (checklist Capa 3, paso 4): 1500 filas de
            // sys.objects, 12 columnas mixtas (int negativo, NULL, char(2) con espacio final,
            // datetime2, bit). A diferencia del test sintetico de mas abajo (una sola columna
            // Id), este ejercita la particion en bloques de 1000 con inferencia de tipos real
            // por columna en cada bloque.
            Skip.IfNot(HasConnection, "SSMSQT_TEST_CONNECTION no esta definida.");

            var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "volumen_1500filas.tsv");
            var data = TsvParser.Parse(File.ReadAllText(fixturePath));
            Assert.Equal(1500, data.Rows.Count);

            var sql = ValuesScriptBuilder.Build(data.Columns.ToList(), data.Rows.ToList());
            Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(sql, "INSERT INTO XXXXXXXX").Count);

            using (var connection = new SqlConnection(ConnectionString))
            {
                connection.Open();
                using (var command = new SqlCommand(ToSelectOnly(sql), connection))
                using (var reader = command.ExecuteReader())
                {
                    var count = 0;
                    do
                    {
                        while (reader.Read())
                        {
                            count++;
                        }
                    } while (reader.NextResult());
                    Assert.Equal(1500, count);
                }
            }
        }

        [SkippableFact]
        public void MilQuinientasFilas_ParticionadoEnBloquesEjecutaYDevuelveTodasLasFilas()
        {
            Skip.IfNot(HasConnection, "SSMSQT_TEST_CONNECTION no esta definida.");

            var columns = new List<string> { "Id" };
            var rows = new List<string[]>();
            for (var i = 0; i < 1500; i++)
            {
                rows.Add(new[] { i.ToString() });
            }

            var sql = ValuesScriptBuilder.Build(columns, rows);

            using (var connection = new SqlConnection(ConnectionString))
            {
                connection.Open();
                using (var command = new SqlCommand(ToSelectOnly(sql), connection))
                using (var reader = command.ExecuteReader())
                {
                    var count = 0;
                    do
                    {
                        while (reader.Read())
                        {
                            count++;
                        }
                    } while (reader.NextResult());
                    Assert.Equal(1500, count);
                }
            }
        }

        [SkippableFact]
        public void TiposMixtos_ScriptParseaSinErrorDeSintaxis()
        {
            Skip.IfNot(HasConnection, "SSMSQT_TEST_CONNECTION no esta definida.");

            var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "tipos_mixtos.tsv");
            var tsv = File.ReadAllText(fixturePath);
            var data = TsvParser.Parse(tsv);
            var sql = ToSelectOnly(ValuesScriptBuilder.Build(data.Columns.ToList(), data.Rows.ToList()));

            using (var connection = new SqlConnection(ConnectionString))
            {
                connection.Open();
                // SET PARSEONLY separa "no compila" (fallaria aca) de "compila pero devuelve
                // datos distintos" (lo verifica el test de arriba). Se usa ToSelectOnly porque
                // "INSERT INTO XXXXXXXX" falla con "Invalid object name" (resolucion de nombre
                // de objeto, no error de sintaxis) incluso bajo PARSEONLY.
                using (var command = new SqlCommand("SET PARSEONLY ON;" + sql + "SET PARSEONLY OFF;", connection))
                {
                    var ex = Record.Exception(() => command.ExecuteNonQuery());
                    Assert.Null(ex);
                }
            }
        }

        // Comparacion a texto normalizado. Solo int/bigint/decimal/bit vuelven con su tipo SQL
        // real (los literales numericos sin comillas SI se tipan sin CAST); datetime2 y
        // uniqueidentifier vuelven como varchar/nvarchar (ver NormalizeExpected), asi que el
        // driver los entrega como string y caen en el default de abajo, sin reformatear.
        private static string NormalizeCell(object value)
        {
            switch (value)
            {
                case decimal dec:
                    return dec.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case bool b:
                    return b ? "1" : "0";
                default:
                    return value?.ToString();
            }
        }

        private static string NormalizeExpected(string raw)
        {
            if (raw == null || raw == "NULL")
            {
                return null;
            }
            // No parsear/reformatear fecha o guid aca: HALLAZGO confirmado con SQL_VARIANT_PROPERTY
            // contra el motor real -- sin CAST explicito, un literal '...' entre comillas (datetime2,
            // uniqueidentifier) NO se convierte a su tipo. Queda varchar/nvarchar tal cual el texto,
            // porque nada en "SELECT * FROM cte" fuerza la conversion. docs/PLAN.md asumia que "el
            // tipo de columna lo infiere el motor a partir de todos los literales de la tabla
            // derivada" -- cierto solo para literales NUMERICOS sin comillas (int/bigint/decimal/bit);
            // para datetime2/uniqueidentifier, el motor los ve como texto. Por eso la comparacion es
            // texto contra texto, no tipo contra tipo.
            return raw;
        }
    }
}
