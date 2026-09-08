using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;

namespace SsmsQuickTools.Features.QuickConnect
{
    public sealed class ServerEntry
    {
        public string Name { get; set; }
        public string Server { get; set; }
        public List<string> Databases { get; set; } = new List<string>();
    }

    [System.Runtime.Serialization.DataContract]
    internal sealed class ConnectionsFile
    {
        [System.Runtime.Serialization.DataMember(Name = "servers")]
        public List<ServerEntryDto> Servers { get; set; } = new List<ServerEntryDto>();
    }

    [System.Runtime.Serialization.DataContract]
    internal sealed class ServerEntryDto
    {
        [System.Runtime.Serialization.DataMember(Name = "name")]
        public string Name { get; set; }

        [System.Runtime.Serialization.DataMember(Name = "server")]
        public string Server { get; set; }

        [System.Runtime.Serialization.DataMember(Name = "databases")]
        public List<string> Databases { get; set; }
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
        private List<ServerEntry> _servers = new List<ServerEntry>();

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

        public IReadOnlyList<ServerEntry> Servers
        {
            get { lock (_lock) { return _servers; } }
        }

        public ServerEntry FindServer(string name)
        {
            return Servers.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public void Reload()
        {
            try
            {
                List<ServerEntry> parsed;
                using (var stream = new FileStream(_configPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var serializer = new DataContractJsonSerializer(typeof(ConnectionsFile));
                    var file = (ConnectionsFile)serializer.ReadObject(stream) ?? new ConnectionsFile();
                    parsed = file.Servers
                        .Where(s => !string.IsNullOrWhiteSpace(s.Name) && !string.IsNullOrWhiteSpace(s.Server))
                        .Select(s => new ServerEntry
                        {
                            Name = s.Name,
                            Server = s.Server,
                            Databases = s.Databases ?? new List<string>(),
                        })
                        .ToList();
                }

                lock (_lock)
                {
                    _servers = parsed;
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
  ""servers"": [
    { ""name"": ""LOCAL"", ""server"": ""localhost"", ""databases"": [ ""master"" ] }
  ]
}";
            File.WriteAllText(path, example, Encoding.UTF8);
        }

        public void Dispose()
        {
            _watcher?.Dispose();
        }
    }
}
