using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SsmsQuickTools.Features.QuickConnect
{
    /// <summary>
    /// Carga, observa y guarda el archivo de configuracion de Quick Connect
    /// (%APPDATA%\SsmsQuickTools\connections.json).
    ///
    /// Soporta autenticacion de Windows y de SQL Server. El password de SQL Server se guarda
    /// cifrado con DPAPI (CurrentUser) via <see cref="SecretProtector"/>; si el archivo se copia a
    /// otro usuario/maquina el password queda ilegible y se pide al conectar, sin romper la carga.
    /// El parseo/serializado en si vive en <see cref="ConnectionsJson"/> (logica pura, con tests).
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

        /// <summary>Copias de las conexiones cargadas: quien las reciba no puede alterar el catalogo.</summary>
        public IReadOnlyList<ConnectionEntry> Connections
        {
            get
            {
                lock (_lock)
                {
                    return _connections.Select(c => c.Clone()).ToList();
                }
            }
        }

        public ConnectionEntry FindConnection(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            lock (_lock)
            {
                return _connections
                    .FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
                    ?.Clone();
            }
        }

        /// <summary>
        /// Inserta o reemplaza (por nombre, case-insensitive) y guarda en disco. Devuelve la entrada
        /// tal como quedo almacenada, con el password ya resuelto.
        /// </summary>
        public ConnectionEntry AddOrUpdate(ConnectionEntry entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            var stored = Normalize(entry);

            var list = Snapshot();
            var index = list.FindIndex(c => string.Equals(c.Name, stored.Name, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                list[index] = stored;
            }
            else
            {
                list.Add(stored);
            }

            Write(list);
            return stored.Clone();
        }

        /// <summary>Elimina por nombre. Devuelve false si no existia.</summary>
        public bool Remove(string name)
        {
            var list = Snapshot();
            var removed = list.RemoveAll(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed)
            {
                Write(list);
            }

            return removed;
        }

        public void Reload()
        {
            try
            {
                string text;
                using (var stream = new FileStream(_configPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                {
                    text = reader.ReadToEnd();
                }

                var parsed = ConnectionsJson.Parse(text, SecretProtector.Unprotect);

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

        private List<ConnectionEntry> Snapshot()
        {
            lock (_lock)
            {
                return _connections.Select(c => c.Clone()).ToList();
            }
        }

        private static ConnectionEntry Normalize(ConnectionEntry entry)
        {
            var stored = entry.Clone();
            stored.Name = (stored.Name ?? string.Empty).Trim();
            stored.Server = (stored.Server ?? string.Empty).Trim();
            stored.Database = (stored.Database ?? string.Empty).Trim();
            stored.UserName = (stored.UserName ?? string.Empty).Trim();

            if (stored.Name.Length == 0)
            {
                throw new ArgumentException("La conexion necesita un nombre.", nameof(entry));
            }

            if (stored.Server.Length == 0)
            {
                throw new ArgumentException("La conexion necesita un servidor.", nameof(entry));
            }

            if (stored.Database.Length == 0)
            {
                throw new ArgumentException("La conexion necesita una base de datos.", nameof(entry));
            }

            if (stored.Authentication == ConnectionAuthentication.SqlServer)
            {
                if (stored.UserName.Length == 0)
                {
                    throw new ArgumentException("SQL Server Authentication necesita un usuario.", nameof(entry));
                }
            }
            else
            {
                stored.UserName = null;
                stored.Password = null;
            }

            return stored;
        }

        private void Write(List<ConnectionEntry> entries)
        {
            var json = ConnectionsJson.Serialize(entries, SecretProtector.Protect);

            // Escritura atomica: el watcher y SSMS pueden estar leyendo el archivo mientras se guarda.
            var tempPath = _configPath + ".tmp";
            File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (File.Exists(_configPath))
            {
                File.Replace(tempPath, _configPath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, _configPath);
            }

            lock (_lock)
            {
                _connections = entries.Select(c => c.Clone()).ToList();
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }

        private static void WriteExampleFile(string path)
        {
            const string example = @"{
  ""connections"": [
    { ""name"": ""LOCAL.master"", ""server"": ""localhost"", ""database"": ""master"", ""authentication"": ""windows"" },
    { ""name"": ""LOCAL.sa"", ""server"": ""localhost"", ""database"": ""master"", ""authentication"": ""sql"", ""user"": ""sa"" }
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
