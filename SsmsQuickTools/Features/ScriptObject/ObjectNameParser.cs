using System;
using System.Text;

namespace SsmsQuickTools.Features.ScriptObject
{
    /// <summary>
    /// Nombre de objeto normalizado: base de datos (opcional), esquema (opcional, default
    /// "dbo") y nombre. Logica pura, sin dependencias de SSMS.
    /// </summary>
    public sealed class ParsedObjectName
    {
        public string Database { get; }
        public string Schema { get; }
        public string Name { get; }

        public ParsedObjectName(string database, string schema, string name)
        {
            Database = database;
            Schema = string.IsNullOrEmpty(schema) ? "dbo" : schema;
            Name = name;
        }

        /// <summary>Nombre entre corchetes calificado por esquema, ej. [dbo].[Clientes].</summary>
        public string QuotedSchemaQualifiedName => $"{Quote(Schema)}.{Quote(Name)}";

        /// <summary>Nombre completo entre corchetes, incluida la base si se especifico.</summary>
        public string QuotedFullName => Database == null
            ? QuotedSchemaQualifiedName
            : $"{Quote(Database)}.{QuotedSchemaQualifiedName}";

        private static string Quote(string identifier) => "[" + identifier.Replace("]", "]]") + "]";
    }

    /// <summary>
    /// Parsea el texto seleccionado en el editor (o la palabra bajo el cursor) como un nombre
    /// de objeto SQL: soporta "db.schema.obj", "[db].[schema].[obj]", "schema.obj", "obj",
    /// con o sin corchetes en cada parte, mezclados.
    /// </summary>
    public static class ObjectNameParser
    {
        public static ParsedObjectName Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var trimmed = text.Trim().TrimEnd(';');
            var parts = SplitParts(trimmed);
            if (parts.Count == 0 || parts.Count > 3)
            {
                return null;
            }

            for (var i = 0; i < parts.Count; i++)
            {
                parts[i] = Unquote(parts[i]);
                if (string.IsNullOrEmpty(parts[i]))
                {
                    return null;
                }
            }

            switch (parts.Count)
            {
                case 1:
                    return new ParsedObjectName(null, null, parts[0]);
                case 2:
                    return new ParsedObjectName(null, parts[0], parts[1]);
                default:
                    return new ParsedObjectName(parts[0], parts[1], parts[2]);
            }
        }

        /// <summary>
        /// Separa por puntos respetando corchetes (un punto dentro de [ ] no separa).
        /// </summary>
        private static System.Collections.Generic.List<string> SplitParts(string text)
        {
            var parts = new System.Collections.Generic.List<string>();
            var current = new StringBuilder();
            var insideBrackets = false;

            foreach (var ch in text)
            {
                if (ch == '[')
                {
                    insideBrackets = true;
                    current.Append(ch);
                }
                else if (ch == ']')
                {
                    insideBrackets = false;
                    current.Append(ch);
                }
                else if (ch == '.' && !insideBrackets)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(ch);
                }
            }
            parts.Add(current.ToString());
            return parts;
        }

        private static string Unquote(string part)
        {
            part = part.Trim();
            if (part.Length >= 2 && part[0] == '[' && part[part.Length - 1] == ']')
            {
                return part.Substring(1, part.Length - 2).Replace("]]", "]");
            }
            return part;
        }
    }
}
