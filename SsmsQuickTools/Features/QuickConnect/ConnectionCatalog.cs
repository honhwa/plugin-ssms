using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;

namespace SsmsQuickTools.Features.QuickConnect
{
    public sealed class ConnectionEntry
    {
        public string Name { get; set; }
        public string Server { get; set; }
        public string Database { get; set; }
    }

    [System.Runtime.Serialization.DataContract]
    internal sealed class ConnectionsFile
    {
        [System.Runtime.Serialization.DataMember(Name = "connections")]
        public List<ConnectionEntryDto> Connections { get; set; } = new List<ConnectionEntryDto>();
    }

    [System.Runtime.Serialization.DataContract]
    internal sealed class ConnectionEntryDto
    {
        [System.Runtime.Serialization.DataMember(Name = "name")]
        public string Name { get; set; }

        [System.Runtime.Serialization.DataMember(Name = "server")]
        public string Server { get; set; }

        [System.Runtime.Serialization.DataMember(Name = "database")]
        public string Database { get; set; }
    }

    /// <summary>
    /// Carga y observa el archivo de configuracion de Quick Connect
    /// (%APPDATA%\SsmsQuickTools\connections.json). Solo autenticacion de Windows: el
    /// archivo no admite usuario/password, a proposito, para no guardar credenciales en disco.
    /// </summary>
    public sealed class ConnectionCatalog : IDisposable
    {
        private readonly string _configPath;
        private readonly FileSystemWatcher _watcher;
        private readonly object _lock = new object();
        private List<ConnectionEntry> _connections = new List<ConnectionEntry>();

        public event EventHandler Changed;

        public ConnectionCatalog()
        {
            var configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SsmsQuickTools");
            Directory.CreateDirectory(configDir);
            _configPath = Path.Combine(configDir, "connections.json");

            if (!File.Exists(_configPath))
            {
                WriteExampleFile(_configPath);
            }

            Reload();

            _watcher = new FileSystemWatcher(configDir, "connections.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            _watcher.Changed += (s, e) => Reload();
            _watcher.Created += (s, e) => Reload();
            _watcher.EnableRaisingEvents = true;
        }

        public string ConfigPath => _configPath;

        public IReadOnlyList<ConnectionEntry> Connections
        {
            get { lock (_lock) { return _connections; } }
        }

        public ConnectionEntry FindConnection(string name)
        {
            return Connections.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public void Reload()
        {
            try
            {
                List<ConnectionEntry> parsed;
                using (var stream = new FileStream(_configPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                using (var noBomStream = new MemoryStream(Encoding.UTF8.GetBytes(reader.ReadToEnd())))
                {
                    // DataContractJsonSerializer no tolera un BOM UTF-8 al inicio del stream
                    // (falla en silencio en ReadObject); se relee como texto y se recodifica sin BOM.
                    var serializer = new DataContractJsonSerializer(typeof(ConnectionsFile));
                    var file = (ConnectionsFile)serializer.ReadObject(noBomStream) ?? new ConnectionsFile();
                    parsed = file.Connections
                        .Where(c => !string.IsNullOrWhiteSpace(c.Name)
                            && !string.IsNullOrWhiteSpace(c.Server)
                            && !string.IsNullOrWhiteSpace(c.Database))
                        .Select(c => new ConnectionEntry
                        {
                            Name = c.Name,
                            Server = c.Server,
                            Database = c.Database,
                        })
                        .ToList();
                }

                lock (_lock)
                {
                    _connections = parsed;
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
                // JSON invalido: se mantiene la ultima configuracion valida cargada.
            }
        }

        private static void WriteExampleFile(string path)
        {
            const string example = @"{
  ""connections"": [
    { ""name"": ""LOCAL.master"", ""server"": ""localhost"", ""database"": ""master"" }
  ]
}";
            File.WriteAllText(path, example, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        public void Dispose()
        {
            _watcher?.Dispose();
        }
    }
}
