using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace SsmsQuickTools.Features.AutoReplacement
{
    /// <summary>
    /// Carga y observa el archivo de configuracion de Auto Replacement
    /// (%APPDATA%\SsmsQuickTools\autoreplacement.xml). Mismo patron que
    /// Features/QuickConnect/ConnectionCatalog.cs.
    /// </summary>
    public sealed class AutoReplacementCatalog : IDisposable
    {
        private readonly string _configPath;
        private readonly FileSystemWatcher _watcher;
        private readonly object _lock = new object();
        private List<AutoReplacementEntry> _entries = new List<AutoReplacementEntry>();

        public event EventHandler Changed;

        public AutoReplacementCatalog()
        {
            var configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SsmsQuickTools");
            Directory.CreateDirectory(configDir);
            _configPath = Path.Combine(configDir, "autoreplacement.xml");

            if (!File.Exists(_configPath))
            {
                WriteExampleFile(_configPath);
            }

            Reload();

            _watcher = new FileSystemWatcher(configDir, "autoreplacement.xml")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            _watcher.Changed += (s, e) => Reload();
            _watcher.Created += (s, e) => Reload();
            _watcher.EnableRaisingEvents = true;
        }

        public string ConfigPath => _configPath;

        public IReadOnlyList<AutoReplacementEntry> Entries
        {
            get { lock (_lock) { return _entries; } }
        }

        /// <summary>
        /// Busca la entrada cuyo conjunto de tokens contiene <paramref name="word"/> (comparacion
        /// de palabra completa, sensible o no a mayusculas segun cada entrada). La primera entrada
        /// del archivo gana en caso de token duplicado entre entradas.
        /// </summary>
        public AutoReplacementEntry FindByToken(string word)
        {
            if (string.IsNullOrEmpty(word))
            {
                return null;
            }

            foreach (var entry in Entries)
            {
                var comparison = entry.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                if (entry.Tokens.Any(t => string.Equals(t, word, comparison)))
                {
                    return entry;
                }
            }

            return null;
        }

        public void Reload()
        {
            try
            {
                List<AutoReplacementEntry> parsed;
                using (var stream = new FileStream(_configPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var document = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
                    parsed = document.Root?.Elements("AutoReplacement")
                        .Select(ParseEntry)
                        .Where(e => e != null)
                        .ToList() ?? new List<AutoReplacementEntry>();
                }

                lock (_lock)
                {
                    _entries = parsed;
                }

                Changed?.Invoke(this, EventArgs.Empty);
            }
            catch (IOException)
            {
                // el archivo puede estar siendo escrito justo cuando se dispara el evento; se
                // ignora, va a quedar la version anterior hasta el proximo cambio detectado.
            }
            catch (Exception)
            {
                // XML invalido: se mantiene la ultima configuracion valida cargada.
            }
        }

        private static AutoReplacementEntry ParseEntry(XElement element)
        {
            var tokens = element.Elements("Token")
                .Select(t => t.Value)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList();

            var replacement = element.Element("Replacement")?.Value;

            if (tokens.Count == 0 || string.IsNullOrEmpty(replacement))
            {
                return null;
            }

            return new AutoReplacementEntry
            {
                Tokens = tokens,
                CaseSensitive = ParseBool(element.Element("CaseSensitive")?.Value),
                Name = element.Element("Name")?.Value,
                Replacement = replacement,
                SelectReplacement = ParseBool(element.Element("SelectReplacement")?.Value),
                CursorPositionMarker = element.Element("CursorPositionMarker")?.Value,
            };
        }

        private static bool ParseBool(string value)
        {
            return bool.TryParse(value, out var result) && result;
        }

        private static void WriteExampleFile(string path)
        {
            const string example = @"<?xml version=""1.0"" encoding=""utf-8""?>
<AutoReplacements>

  <AutoReplacement>
    <Token>cm</Token>
    <Token>colamen</Token>
    <CaseSensitive>false</CaseSensitive>
    <Name>Cola Mensajes</Name>
    <Replacement>SELECT TOP 200 * FROM dbo.cola_mensajes_n3 WITH(NOLOCK) WHERE 1=1 #
-- AND id_linea = 11111111
ORDER BY id_mensaje DESC</Replacement>
    <SelectReplacement>false</SelectReplacement>
    <CursorPositionMarker>#</CursorPositionMarker>
  </AutoReplacement>

</AutoReplacements>";
            File.WriteAllText(path, example, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        public void Dispose()
        {
            _watcher?.Dispose();
        }
    }
}
