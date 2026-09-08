using System.Text.RegularExpressions;

namespace SsmsQuickTools.Features.ScriptObject
{
    /// <summary>
    /// Reemplaza el primer CREATE por ALTER en la definicion de un objeto programable
    /// (procedimiento, funcion, trigger, vista), respetando comentarios previos al CREATE.
    /// Logica pura, sin dependencias de SSMS/SMO.
    /// </summary>
    public static class CreateAlterRewriter
    {
        public static string ReplaceCreateWithAlter(string definition)
        {
            var match = Regex.Match(definition, @"\bCREATE\b", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return definition;
            }

            return definition.Substring(0, match.Index) + "ALTER" + definition.Substring(match.Index + match.Length);
        }
    }
}
