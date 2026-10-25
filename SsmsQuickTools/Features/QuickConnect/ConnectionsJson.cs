using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace SsmsQuickTools.Features.QuickConnect
{
    /// <summary>
    /// Lectura y escritura de <c>connections.json</c>. Logica pura (sin dependencias de SSMS ni de
    /// VS) para poder testearla: el cifrado del password se inyecta como delegate, de modo que los
    /// tests usan un protector falso y la extension usa <see cref="SecretProtector"/>.
    /// </summary>
    public static class ConnectionsJson
    {
        public const string SqlAuthenticationValue = "sql";
        public const string WindowsAuthenticationValue = "windows";

        /// <summary>
        /// Parsea el contenido del archivo. Las entradas incompletas (sin name/server/database) se
        /// descartan, igual que antes; los nombres duplicados se resuelven quedando el primero
        /// (case-insensitive), porque el combo y el administrador identifican las conexiones por nombre.
        /// Tira <see cref="SerializationException"/> si el JSON es invalido: el llamador decide si
        /// conserva la ultima configuracion valida.
        /// </summary>
        public static List<ConnectionEntry> Parse(string json, Func<string, string> decryptPassword)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<ConnectionEntry>();
            }

            if (decryptPassword == null)
            {
                decryptPassword = v => v;
            }

            var bytes = Encoding.UTF8.GetBytes(json.TrimStart('\uFEFF'));
            ConnectionsFileDto file;
            using (var stream = new MemoryStream(bytes))
            {
                var serializer = new DataContractJsonSerializer(typeof(ConnectionsFileDto));
                file = serializer.ReadObject(stream) as ConnectionsFileDto;
            }

            var result = new List<ConnectionEntry>();
            if (file?.Connections == null)
            {
                return result;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dto in file.Connections)
            {
                if (dto == null)
                {
                    continue;
                }

                var name = (dto.Name ?? string.Empty).Trim();
                var server = (dto.Server ?? string.Empty).Trim();
                var database = (dto.Database ?? string.Empty).Trim();
                if (name.Length == 0 || server.Length == 0 || database.Length == 0)
                {
                    continue;
                }

                if (!seen.Add(name))
                {
                    continue;
                }

                var entry = new ConnectionEntry
                {
                    Name = name,
                    Server = server,
                    Database = database,
                    Authentication = ParseAuthentication(dto.Authentication),
                };

                if (entry.Authentication == ConnectionAuthentication.SqlServer)
                {
                    entry.UserName = (dto.User ?? string.Empty).Trim();
                    entry.Password = decryptPassword(dto.Password);
                    // Sin usuario no hay SQL auth posible: se degrada a Windows para no ofrecer en el
                    // combo una conexion que siempre va a fallar al pedir credenciales.
                    if (entry.UserName.Length == 0)
                    {
                        entry.Authentication = ConnectionAuthentication.Windows;
                        entry.UserName = null;
                        entry.Password = null;
                    }
                }

                result.Add(entry);
            }

            return result;
        }

        /// <summary>
        /// Serializa el catalogo con un objeto por linea (2 espacios de sangria), para que el
        /// archivo siga siendo comodo de editar a mano. El password se cifra con
        /// <paramref name="encryptPassword"/> y solo se escribe para conexiones SQL.
        /// </summary>
        public static string Serialize(IEnumerable<ConnectionEntry> entries, Func<string, string> encryptPassword)
        {
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            if (encryptPassword == null)
            {
                encryptPassword = v => v;
            }

            var lines = entries
                .Where(e => e != null)
                .Select(e => "    " + SerializeEntry(e, encryptPassword))
                .ToList();

            var sb = new StringBuilder();
            sb.Append("{\r\n  \"connections\": [");
            if (lines.Count == 0)
            {
                sb.Append("]\r\n}\r\n");
                return sb.ToString();
            }

            sb.Append("\r\n");
            sb.Append(string.Join(",\r\n", lines));
            sb.Append("\r\n  ]\r\n}\r\n");
            return sb.ToString();
        }

        public static ConnectionAuthentication ParseAuthentication(string value)
        {
            var normalized = (value ?? string.Empty).Trim();
            if (normalized.Equals(SqlAuthenticationValue, StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("sqlserver", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("sql server", StringComparison.OrdinalIgnoreCase))
            {
                return ConnectionAuthentication.SqlServer;
            }

            // "windows", "integrated", vacio o desconocido: Windows, que es lo unico que existia
            // antes de agregar autenticacion SQL (compatibilidad con archivos viejos).
            return ConnectionAuthentication.Windows;
        }

        public static string AuthenticationToString(ConnectionAuthentication authentication)
        {
            return authentication == ConnectionAuthentication.SqlServer
                ? SqlAuthenticationValue
                : WindowsAuthenticationValue;
        }

        private static string SerializeEntry(ConnectionEntry entry, Func<string, string> encryptPassword)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            AppendProperty(sb, "name", entry.Name, first: true);
            AppendProperty(sb, "server", entry.Server, first: false);
            AppendProperty(sb, "database", entry.Database, first: false);
            AppendProperty(sb, "authentication", AuthenticationToString(entry.Authentication), first: false);

            if (entry.Authentication == ConnectionAuthentication.SqlServer)
            {
                AppendProperty(sb, "user", entry.UserName, first: false);

                var stored = encryptPassword(entry.Password);
                if (!string.IsNullOrEmpty(stored))
                {
                    AppendProperty(sb, "password", stored, first: false);
                }
            }

            sb.Append(" }");
            return sb.ToString();
        }

        private static void AppendProperty(StringBuilder sb, string name, string value, bool first)
        {
            if (!first)
            {
                sb.Append(", ");
            }

            sb.Append('"').Append(name).Append("\": \"");
            AppendEscaped(sb, value ?? string.Empty);
            sb.Append('"');
        }

        private static void AppendEscaped(StringBuilder sb, string value)
        {
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            sb.Append(c);
                        }

                        break;
                }
            }
        }

        [DataContract]
        private sealed class ConnectionsFileDto
        {
            [DataMember(Name = "connections")]
            public List<ConnectionEntryDto> Connections { get; set; }
        }

        [DataContract]
        private sealed class ConnectionEntryDto
        {
            [DataMember(Name = "name")]
            public string Name { get; set; }

            [DataMember(Name = "server")]
            public string Server { get; set; }

            [DataMember(Name = "database")]
            public string Database { get; set; }

            [DataMember(Name = "authentication")]
            public string Authentication { get; set; }

            [DataMember(Name = "user")]
            public string User { get; set; }

            [DataMember(Name = "password")]
            public string Password { get; set; }
        }
    }
}
