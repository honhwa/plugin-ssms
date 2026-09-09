using System;
using System.ComponentModel.Design;
using System.Data;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
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
                // Database Engine: sin este GUID, VerifyConnectionInfo (ConnectionDlg.dll) rechaza
                // la conexion con "Tipo de conexion inesperado".
                ServerType = new Guid("8c91a03d-f9b4-46c0-a305-b5dcc79ff907"),
            };
            connectionInfo.AdvancedOptions["DATABASE"] = _selectedDatabaseName;

            // Reconecta in-place la ventana activa (mismo servidor: solo cambia de base; servidor
            // distinto: desconecta y reconecta). OpenConnection solo se invoca cuando SsmsHost
            // necesita una conexion ADO.NET nueva para una reconexion completa; en ese caso la
            // conexion queda en poder de SsmsHost (adoptada por la ventana si tiene exito,
            // descartada si falla).
            var outcome = SsmsHost.TryReconnectActiveWindow(
                connectionInfo,
                _selectedDatabaseName,
                () => OpenConnection(connectionInfo, _selectedDatabaseName),
                out var reason);

            switch (outcome)
            {
                case ReconnectOutcome.DatabaseChanged:
                case ReconnectOutcome.Reconnected:
                    return;

                case ReconnectOutcome.NoQueryWindow:
                    // No hay ninguna ventana de query activa: se abre una nueva ya conectada, con
                    // una conexion ADO.NET propia (SsmsHost no abrio ninguna para este caso).
                    var dbConnection = OpenConnection(connectionInfo, _selectedDatabaseName);
                    try
                    {
                        SsmsHost.OpenNewScriptWindow(null, connectionInfo, dbConnection);
                    }
                    catch
                    {
                        dbConnection.Dispose();
                        throw;
                    }
                    return;

                case ReconnectOutcome.Blocked:
                case ReconnectOutcome.Failed:
                    MessageBox.Show(reason, "SSMS Quick Tools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
            }
        }

        private static IDbConnection OpenConnection(UIConnectionInfo connectionInfo, string database)
        {
            var dbConnection = new Microsoft.Data.SqlClient.SqlConnection(
                SsmsHost.BuildConnectionString(connectionInfo, database));
            try
            {
                dbConnection.Open();
                return dbConnection;
            }
            catch
            {
                dbConnection.Dispose();
                throw;
            }
        }
    }
}
