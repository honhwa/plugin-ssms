using System;
using System.ComponentModel.Design;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;
using SsmsQuickTools.Ssms;

namespace SsmsQuickTools.Features.QuickConnect
{
    /// <summary>
    /// Combo "Quick Connections" de la toolbar. Al elegir una conexion, reconecta la ventana de
    /// query activa (cambia conexion + hace USE de la base indicada en connections.json).
    /// Soporta autenticacion de Windows y de SQL Server; cuando la entrada SQL no tiene password
    /// utilizable (nunca se guardo, o el blob DPAPI no se pudo descifrar) se pide antes de conectar.
    /// </summary>
    public sealed class QuickConnectCommands
    {
        private readonly AsyncPackage _package;
        private readonly OleMenuCommandService _commandService;
        private readonly ConnectionCatalog _catalog;

        private string _selectedConnectionName;

        public QuickConnectCommands(AsyncPackage package, OleMenuCommandService commandService, ConnectionCatalog catalog)
        {
            _package = package;
            _commandService = commandService;
            _catalog = catalog;
        }

        public void Register()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            AddCommand(PkgCmdId.ConnectionCombo, OnConnectionCombo);
            AddCommand(PkgCmdId.ConnectionComboGetList, OnConnectionComboGetList);
        }

        private void AddCommand(uint id, EventHandler handler)
        {
            var commandId = new CommandID(PackageGuids.QuickToolsCmdSet, (int)id);
            var command = new OleMenuCommand(handler, commandId);
            _commandService.AddCommand(command);
        }

        private void OnConnectionCombo(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!(e is OleMenuCmdEventArgs args))
            {
                return;
            }

            if (args.InValue is string newChoice)
            {
                var connection = _catalog.FindConnection(newChoice);
                if (connection == null)
                {
                    return;
                }

                _selectedConnectionName = connection.Name;
                ConnectToSelection();
                return;
            }

            if (args.OutValue != IntPtr.Zero)
            {
                Marshal.GetNativeVariantForObject(_selectedConnectionName ?? string.Empty, args.OutValue);
            }
        }

        private void OnConnectionComboGetList(object sender, EventArgs e)
        {
            if (!(e is OleMenuCmdEventArgs args) || args.OutValue == IntPtr.Zero)
            {
                return;
            }

            var names = _catalog.Connections.Select(c => c.Name).ToArray();
            Marshal.GetNativeVariantForObject(names, args.OutValue);
        }

        private void ConnectToSelection()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var connection = _catalog.FindConnection(_selectedConnectionName);
            if (connection == null)
            {
                return;
            }

            if (!EnsurePassword(connection))
            {
                return;
            }

            var connectionInfo = QuickConnectionFactory.CreateConnectionInfo(connection);

            // Reconecta in-place la ventana activa (mismo servidor: solo cambia de base; servidor
            // distinto: desconecta y reconecta). OpenConnection solo se invoca cuando SsmsHost
            // necesita una conexion ADO.NET nueva para una reconexion completa; en ese caso la
            // conexion queda en poder de SsmsHost (adoptada por la ventana si tiene exito,
            // descartada si falla).
            var outcome = SsmsHost.TryReconnectActiveWindow(
                connectionInfo,
                connection.Database,
                () => QuickConnectionFactory.Open(connectionInfo, connection.Database),
                out var reason);

            switch (outcome)
            {
                case ReconnectOutcome.DatabaseChanged:
                case ReconnectOutcome.Reconnected:
                    return;

                case ReconnectOutcome.NoQueryWindow:
                    // No hay ninguna ventana de query activa: se abre una nueva ya conectada, con
                    // una conexion ADO.NET propia (SsmsHost no abrio ninguna para este caso).
                    var dbConnection = QuickConnectionFactory.Open(connectionInfo, connection.Database);
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

        /// <summary>
        /// Asegura que una conexion SQL tenga password antes de usarla. Devuelve false solo si el
        /// usuario cancela el dialogo (en ese caso no se intenta conectar).
        /// </summary>
        private bool EnsurePassword(ConnectionEntry connection)
        {
            if (!connection.NeedsPassword)
            {
                return true;
            }

            using (var dialog = new PasswordPromptDialog(connection))
            {
                if (dialog.ShowDialog() != DialogResult.OK)
                {
                    return false;
                }

                connection.Password = dialog.Password;

                if (dialog.RememberPassword)
                {
                    try
                    {
                        _catalog.AddOrUpdate(connection);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            "No se pudo guardar el password en connections.json: " + ex.Message,
                            "SSMS Quick Tools",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }
                }

                return true;
            }
        }
    }
}
