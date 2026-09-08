using System;
using System.ComponentModel.Design;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.SqlServer.Management.Smo.RegSvrEnum;
using Microsoft.VisualStudio.Shell;
using SsmsQuickTools.Ssms;

namespace SsmsQuickTools.Features.QuickConnect
{
    /// <summary>
    /// Combos "Servidor" / "Base de datos" de la toolbar Quick Connect. Al elegir una base de
    /// datos, reconecta la ventana de query activa (cambia conexion + hace USE de la base).
    /// </summary>
    public sealed class QuickConnectCommands
    {
        private readonly AsyncPackage _package;
        private readonly OleMenuCommandService _commandService;
        private readonly ConnectionCatalog _catalog;

        private string _selectedServerName;
        private string _selectedDatabaseName;

        public QuickConnectCommands(AsyncPackage package, OleMenuCommandService commandService, ConnectionCatalog catalog)
        {
            _package = package;
            _commandService = commandService;
            _catalog = catalog;
        }

        public void Register()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            AddCommand(PkgCmdId.ServerCombo, OnServerCombo);
            AddCommand(PkgCmdId.ServerComboGetList, OnServerComboGetList);
            AddCommand(PkgCmdId.DatabaseCombo, OnDatabaseCombo);
            AddCommand(PkgCmdId.DatabaseComboGetList, OnDatabaseComboGetList);
        }

        private void AddCommand(uint id, EventHandler handler)
        {
            var commandId = new CommandID(PackageGuids.QuickToolsCmdSet, (int)id);
            var command = new OleMenuCommand(handler, commandId);
            _commandService.AddCommand(command);
        }

        private void OnServerCombo(object sender, EventArgs e)
        {
            if (!(e is OleMenuCmdEventArgs args))
            {
                return;
            }

            if (args.InValue is string newChoice)
            {
                var server = _catalog.FindServer(newChoice);
                _selectedServerName = server?.Name;
                _selectedDatabaseName = null; // se resetea la base al cambiar de servidor
                return;
            }

            if (args.OutValue != IntPtr.Zero)
            {
                Marshal.GetNativeVariantForObject(_selectedServerName ?? string.Empty, args.OutValue);
            }
        }

        private void OnServerComboGetList(object sender, EventArgs e)
        {
            if (!(e is OleMenuCmdEventArgs args) || args.OutValue == IntPtr.Zero)
            {
                return;
            }

            var names = _catalog.Servers.Select(s => s.Name).ToArray();
            Marshal.GetNativeVariantForObject(names, args.OutValue);
        }

        private void OnDatabaseCombo(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!(e is OleMenuCmdEventArgs args))
            {
                return;
            }

            if (args.InValue is string newChoice)
            {
                _selectedDatabaseName = newChoice;
                ConnectToSelection();
                return;
            }

            if (args.OutValue != IntPtr.Zero)
            {
                Marshal.GetNativeVariantForObject(_selectedDatabaseName ?? string.Empty, args.OutValue);
            }
        }

        private void OnDatabaseComboGetList(object sender, EventArgs e)
        {
            if (!(e is OleMenuCmdEventArgs args) || args.OutValue == IntPtr.Zero)
            {
                return;
            }

            var server = _catalog.FindServer(_selectedServerName);
            var databases = server?.Databases?.ToArray() ?? Array.Empty<string>();
            Marshal.GetNativeVariantForObject(databases, args.OutValue);
        }

        private void ConnectToSelection()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var server = _catalog.FindServer(_selectedServerName);
            if (server == null || string.IsNullOrEmpty(_selectedDatabaseName))
            {
                return;
            }

            var connectionInfo = new UIConnectionInfo
            {
                ServerName = server.Server,
                AuthenticationType = 0, // Windows Authentication
                ApplicationName = "SsmsQuickTools",
            };
            connectionInfo.AdvancedOptions["DATABASE"] = _selectedDatabaseName;

            if (SsmsHost.TrySetActiveWindowConnection(connectionInfo, out var failureReason))
            {
                return;
            }

            // Sin ventana de query activa (o no reconectable): se abre una nueva ya conectada
            // a la base elegida en lugar de fallar en silencio.
            SsmsHost.OpenNewScriptWindow(null, connectionInfo);
        }
    }
}
