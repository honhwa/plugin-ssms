using System;
using System.Linq;
using System.Runtime.Serialization;
using SsmsQuickTools.Features.QuickConnect;
using Xunit;

namespace SsmsQuickTools.Tests
{
    public class ConnectionsJsonTests
    {
        private const string LegacyWindowsJson = @"{
  ""connections"": [
    { ""name"": ""LOCAL.master"", ""server"": ""localhost"", ""database"": ""master"" }
  ]
}";

        [Fact]
        public void Parse_SinCampoAuthentication_EsWindowsYNoGuardaCredenciales()
        {
            var entries = ConnectionsJson.Parse(LegacyWindowsJson, v => v);

            var entry = Assert.Single(entries);
            Assert.Equal("LOCAL.master", entry.Name);
            Assert.Equal("localhost", entry.Server);
            Assert.Equal("master", entry.Database);
            Assert.Equal(ConnectionAuthentication.Windows, entry.Authentication);
            Assert.Null(entry.UserName);
            Assert.Null(entry.Password);
            Assert.False(entry.NeedsPassword);
        }

        [Fact]
        public void Parse_EntradaSql_DescifraPasswordConElDelegate()
        {
            const string json = @"{
  ""connections"": [
    { ""name"": ""PROD.SQL"", ""server"": ""sql01"", ""database"": ""Ventas"",
      ""authentication"": ""sql"", ""user"": ""app_ventas"", ""password"": ""cifrado"" }
  ]
}";

            var entries = ConnectionsJson.Parse(json, v => v == "cifrado" ? "P@ssw0rd" : null);

            var entry = Assert.Single(entries);
            Assert.Equal(ConnectionAuthentication.SqlServer, entry.Authentication);
            Assert.Equal("app_ventas", entry.UserName);
            Assert.Equal("P@ssw0rd", entry.Password);
            Assert.False(entry.NeedsPassword);
        }

        [Fact]
        public void Parse_PasswordIndescifrable_QuedaNullYPidePassword()
        {
            const string json = @"{
  ""connections"": [
    { ""name"": ""PROD.SQL"", ""server"": ""sql01"", ""database"": ""Ventas"",
      ""authentication"": ""sql"", ""user"": ""app_ventas"", ""password"": ""dpapi:otroUsuario"" }
  ]
}";

            var entries = ConnectionsJson.Parse(json, v => null);

            var entry = Assert.Single(entries);
            Assert.Equal(ConnectionAuthentication.SqlServer, entry.Authentication);
            Assert.Null(entry.Password);
            Assert.True(entry.NeedsPassword);
        }

        [Fact]
        public void Parse_SqlSinUsuario_DegradaAWindows()
        {
            const string json = @"{
  ""connections"": [
    { ""name"": ""RARO"", ""server"": ""sql01"", ""database"": ""Ventas"",
      ""authentication"": ""sql"", ""password"": ""x"" }
  ]
}";

            var entry = Assert.Single(ConnectionsJson.Parse(json, v => v));
            Assert.Equal(ConnectionAuthentication.Windows, entry.Authentication);
            Assert.Null(entry.UserName);
            Assert.Null(entry.Password);
        }

        [Theory]
        [InlineData("SQL")]
        [InlineData("sqlserver")]
        [InlineData("SQL Server")]
        public void Parse_AuthenticationSql_EsCaseInsensitive(string value)
        {
            var json = @"{ ""connections"": [ { ""name"": ""A"", ""server"": ""s"", ""database"": ""d"", " +
                       @"""authentication"": """ + value + @""", ""user"": ""u"" } ] }";

            var entry = Assert.Single(ConnectionsJson.Parse(json, v => v));
            Assert.Equal(ConnectionAuthentication.SqlServer, entry.Authentication);
        }

        [Fact]
        public void Parse_AuthenticationDesconocida_CaeAWindows()
        {
            const string json = @"{
  ""connections"": [
    { ""name"": ""A"", ""server"": ""s"", ""database"": ""d"", ""authentication"": ""entra"" }
  ]
}";

            var entry = Assert.Single(ConnectionsJson.Parse(json, v => v));
            Assert.Equal(ConnectionAuthentication.Windows, entry.Authentication);
        }

        [Fact]
        public void Parse_EntradasIncompletas_SeDescartan()
        {
            const string json = @"{
  ""connections"": [
    { ""name"": """", ""server"": ""s"", ""database"": ""d"" },
    { ""name"": ""B"", ""server"": """", ""database"": ""d"" },
    { ""name"": ""C"", ""server"": ""s"", ""database"": """" },
    { ""name"": ""OK"", ""server"": ""s"", ""database"": ""d"" }
  ]
}";

            var entries = ConnectionsJson.Parse(json, v => v);

            var entry = Assert.Single(entries);
            Assert.Equal("OK", entry.Name);
        }

        [Fact]
        public void Parse_NombresDuplicados_SeQuedaElPrimero()
        {
            const string json = @"{
  ""connections"": [
    { ""name"": ""PROD"", ""server"": ""a"", ""database"": ""d1"" },
    { ""name"": ""prod"", ""server"": ""b"", ""database"": ""d2"" }
  ]
}";

            var entry = Assert.Single(ConnectionsJson.Parse(json, v => v));
            Assert.Equal("a", entry.Server);
            Assert.Equal("d1", entry.Database);
        }

        [Fact]
        public void Parse_BomAlInicio_NoRompeElParseo()
        {
            var entries = ConnectionsJson.Parse("\uFEFF" + LegacyWindowsJson, v => v);
            Assert.Single(entries);
        }

        [Fact]
        public void Parse_JsonInvalido_TiraSerializationException()
        {
            Assert.Throws<SerializationException>(() => ConnectionsJson.Parse("{ roto", v => v));
        }

        [Fact]
        public void Parse_JsonVacio_DevuelveListaVacia()
        {
            Assert.Empty(ConnectionsJson.Parse(string.Empty, v => v));
            Assert.Empty(ConnectionsJson.Parse("{}", v => v));
        }

        [Fact]
        public void Serialize_EntradaWindows_OmiteUsuarioYPassword()
        {
            var json = ConnectionsJson.Serialize(
                new[] { new ConnectionEntry { Name = "A", Server = "s", Database = "d" } },
                v => "NO-DEBERIA-USARSE");

            Assert.Contains(@"""authentication"": ""windows""", json);
            Assert.DoesNotContain("\"user\"", json);
            Assert.DoesNotContain("password", json);
            Assert.DoesNotContain("NO-DEBERIA-USARSE", json);
        }

        [Fact]
        public void Serialize_EntradaSql_CifraElPassword()
        {
            var json = ConnectionsJson.Serialize(
                new[]
                {
                    new ConnectionEntry
                    {
                        Name = "A",
                        Server = "s",
                        Database = "d",
                        Authentication = ConnectionAuthentication.SqlServer,
                        UserName = "u",
                        Password = "secreto",
                    },
                },
                v => "ENC(" + v + ")");

            Assert.Contains(@"""authentication"": ""sql""", json);
            Assert.Contains(@"""user"": ""u""", json);
            Assert.Contains(@"""password"": ""ENC(secreto)""", json);
            Assert.DoesNotContain(@"""password"": ""secreto""", json);
        }

        [Fact]
        public void Serialize_SqlSinPassword_OmiteElCampo()
        {
            var json = ConnectionsJson.Serialize(
                new[]
                {
                    new ConnectionEntry
                    {
                        Name = "A",
                        Server = "s",
                        Database = "d",
                        Authentication = ConnectionAuthentication.SqlServer,
                        UserName = "u",
                        Password = null,
                    },
                },
                v => null);

            Assert.DoesNotContain("password", json);
        }

        [Fact]
        public void Serialize_EscapaComillasYBackslash()
        {
            var json = ConnectionsJson.Serialize(
                new[] { new ConnectionEntry { Name = "A\"B", Server = @"srv\inst", Database = "d" } },
                v => v);

            Assert.Contains(@"""name"": ""A\""B""", json);
            Assert.Contains(@"""server"": ""srv\\inst""", json);
        }

        [Fact]
        public void Serialize_ListaVacia_EscribeArrayVacio()
        {
            var json = ConnectionsJson.Serialize(Array.Empty<ConnectionEntry>(), v => v);
            Assert.Contains(@"""connections"": []", json);
        }

        [Fact]
        public void RoundTrip_PreservaOrdenYAutenticacion()
        {
            var original = new[]
            {
                new ConnectionEntry { Name = "WIN", Server = "s1", Database = "d1" },
                new ConnectionEntry
                {
                    Name = "SQL",
                    Server = "s2",
                    Database = "d2",
                    Authentication = ConnectionAuthentication.SqlServer,
                    UserName = "sa",
                    Password = "P@ss\"word",
                },
            };

            var json = ConnectionsJson.Serialize(original, v => "enc:" + v);
            var parsed = ConnectionsJson.Parse(json, v => v.StartsWith("enc:", StringComparison.Ordinal) ? v.Substring(4) : null);

            Assert.Equal(new[] { "WIN", "SQL" }, parsed.Select(e => e.Name).ToArray());
            Assert.Equal(ConnectionAuthentication.Windows, parsed[0].Authentication);
            Assert.Equal(ConnectionAuthentication.SqlServer, parsed[1].Authentication);
            Assert.Equal("sa", parsed[1].UserName);
            Assert.Equal("P@ss\"word", parsed[1].Password);
        }

        [Fact]
        public void Serialize_UnObjetoPorLinea_ParaEdicionManual()
        {
            var json = ConnectionsJson.Serialize(
                new[]
                {
                    new ConnectionEntry { Name = "A", Server = "s", Database = "d" },
                    new ConnectionEntry { Name = "B", Server = "s", Database = "d" },
                },
                v => v);

            var lines = json.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(6, lines.Length);
            Assert.StartsWith("    {", lines[2]);
            Assert.EndsWith("},", lines[2]);
            Assert.StartsWith("    {", lines[3]);
            Assert.Equal("  ]", lines[4]);
            Assert.Equal("}", lines[5]);
        }
    }

    public class SecretProtectorTests
    {
        [Fact]
        public void Protect_Y_Unprotect_DevuelvenElMismoTexto()
        {
            const string password = "P@ssw0rd con espacios y acentos: ñ";

            var stored = SecretProtector.Protect(password);

            Assert.StartsWith(SecretProtector.Prefix, stored);
            Assert.DoesNotContain(password, stored);
            Assert.Equal(password, SecretProtector.Unprotect(stored));
        }

        [Fact]
        public void Protect_TextoVacio_DevuelveNull()
        {
            Assert.Null(SecretProtector.Protect(null));
            Assert.Null(SecretProtector.Protect(string.Empty));
        }

        [Fact]
        public void Unprotect_SinPrefijo_DevuelveElValorTalCual()
        {
            // Permite escribir el password a mano en connections.json; al guardar desde la UI se cifra.
            Assert.Equal("a-mano", SecretProtector.Unprotect("a-mano"));
        }

        [Fact]
        public void Unprotect_Base64Invalido_DevuelveNull()
        {
            Assert.Null(SecretProtector.Unprotect(SecretProtector.Prefix + "@@@no-es-base64@@@"));
        }

        [Fact]
        public void Unprotect_BlobDeOtraCuenta_DevuelveNull()
        {
            // Bytes arbitrarios: DPAPI falla con CryptographicException y no debe propagarse.
            var basura = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            Assert.Null(SecretProtector.Unprotect(SecretProtector.Prefix + basura));
        }

        [Fact]
        public void Unprotect_Vacio_DevuelveNull()
        {
            Assert.Null(SecretProtector.Unprotect(null));
            Assert.Null(SecretProtector.Unprotect(string.Empty));
        }
    }
}
