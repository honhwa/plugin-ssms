using System;
using Microsoft.Data.SqlClient;
using System.Text;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;

namespace SsmsQuickTools.Features.ScriptObject
{
    public enum SsmsObjectKind
    {
        Unknown,
        Table,
        View,
        Procedure,
        Function,
        Trigger,
    }

    public sealed class ScriptResult
    {
        public string Sql { get; }
        public SsmsObjectKind Kind { get; }

        public ScriptResult(string sql, SsmsObjectKind kind)
        {
            Sql = sql;
            Kind = kind;
        }
    }

    /// <summary>
    /// Genera scripts de CREATE/ALTER para un objeto SQL, usando una conexion ADO.NET propia
    /// (independiente de la conexion "viva" de SSMS). Objetos programables (procedimientos,
    /// funciones, triggers, vistas) usan OBJECT_DEFINITION; tablas usan SMO Scripter, que no
    /// admite ALTER (no existe un "ALTER TABLE" generico que reproduzca toda la definicion).
    /// </summary>
    public static class ObjectScripter
    {
        public static ScriptResult GenerateCreate(ParsedObjectName name, string connectionString, out string error)
        {
            return Generate(name, connectionString, wantAlter: false, out error);
        }

        public static ScriptResult GenerateAlter(ParsedObjectName name, string connectionString, out string error)
        {
            return Generate(name, connectionString, wantAlter: true, out error);
        }

        private static ScriptResult Generate(ParsedObjectName name, string connectionString, bool wantAlter, out string error)
        {
            using (var connection = new SqlConnection(connectionString))
            {
                try
                {
                    connection.Open();
                }
                catch (Exception ex)
                {
                    error = "No se pudo conectar para generar el script: " + ex.Message;
                    return null;
                }

                var kind = ResolveKind(connection, name, out var notFoundReason);
                if (kind == SsmsObjectKind.Unknown)
                {
                    error = notFoundReason;
                    return null;
                }

                if (kind == SsmsObjectKind.Table)
                {
                    if (wantAlter)
                    {
                        error = "No existe un ALTER generico para tablas (no reproduce toda la definicion). " +
                                "Usa Generar CREATE, o el diseñador de tablas de SSMS para modificarla.";
                        return null;
                    }

                    return GenerateTableCreate(connection, name, out error);
                }

                return GenerateProgrammableObjectScript(connection, name, kind, wantAlter, out error);
            }
        }

        private static SsmsObjectKind ResolveKind(SqlConnection connection, ParsedObjectName name, out string error)
        {
            const string query = @"
SELECT o.type
FROM sys.objects AS o
WHERE o.object_id = OBJECT_ID(@qualifiedName);";

            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@qualifiedName", name.QuotedSchemaQualifiedName);
                var typeCode = (command.ExecuteScalar() as string)?.Trim();

                if (typeCode == null)
                {
                    error = $"No se encontro el objeto {name.QuotedSchemaQualifiedName} en la base " +
                             $"{(connection.Database ?? "(desconocida)")}.";
                    return SsmsObjectKind.Unknown;
                }

                error = null;
                switch (typeCode)
                {
                    case "U": return SsmsObjectKind.Table;
                    case "V": return SsmsObjectKind.View;
                    case "P": return SsmsObjectKind.Procedure;
                    case "FN":
                    case "IF":
                    case "TF": return SsmsObjectKind.Function;
                    case "TR": return SsmsObjectKind.Trigger;
                    default:
                        error = $"Tipo de objeto no soportado ({typeCode}) para {name.QuotedSchemaQualifiedName}.";
                        return SsmsObjectKind.Unknown;
                }
            }
        }

        private static ScriptResult GenerateProgrammableObjectScript(SqlConnection connection, ParsedObjectName name, SsmsObjectKind kind, bool wantAlter, out string error)
        {
            const string query = "SELECT OBJECT_DEFINITION(OBJECT_ID(@qualifiedName));";
            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@qualifiedName", name.QuotedSchemaQualifiedName);
                var definition = command.ExecuteScalar() as string;

                if (string.IsNullOrWhiteSpace(definition))
                {
                    error = $"El objeto {name.QuotedSchemaQualifiedName} no tiene definicion visible " +
                             "(¿es un objeto cifrado, o del sistema?).";
                    return null;
                }

                var sql = wantAlter ? CreateAlterRewriter.ReplaceCreateWithAlter(definition) : definition.TrimStart();
                error = null;
                return new ScriptResult(sql.TrimEnd() + Environment.NewLine + "GO" + Environment.NewLine, kind);
            }
        }

        private static ScriptResult GenerateTableCreate(SqlConnection connection, ParsedObjectName name, out string error)
        {
            try
            {
                var serverConnection = new ServerConnection(connection);
                var server = new Server(serverConnection);
                var database = server.Databases[connection.Database];
                if (database == null)
                {
                    error = $"No se encontro la base {connection.Database}.";
                    return null;
                }

                var table = database.Tables[name.Name, name.Schema];
                if (table == null)
                {
                    error = $"No se encontro la tabla {name.QuotedSchemaQualifiedName}.";
                    return null;
                }

                var options = new ScriptingOptions
                {
                    ScriptDrops = false,
                    IncludeIfNotExists = false,
                    Indexes = true,
                    DriAll = true,
                    Triggers = false,
                    SchemaQualify = true,
                    IncludeHeaders = false,
                };

                var script = new StringBuilder();
                foreach (string line in table.Script(options))
                {
                    script.AppendLine(line);
                    script.AppendLine("GO");
                }

                error = null;
                return new ScriptResult(script.ToString(), SsmsObjectKind.Table);
            }
            catch (Exception ex)
            {
                error = "No se pudo generar el script de la tabla con SMO: " + ex.Message;
                return null;
            }
        }
    }
}
