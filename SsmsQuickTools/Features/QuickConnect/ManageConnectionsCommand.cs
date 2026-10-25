using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;

namespace SsmsQuickTools.Features.QuickConnect
{
    /// <summary>
    /// Comando "Manage Quick Connections..." del menu Quick Tools: abre el administrador de
    /// conexiones (alta/edicion/baja sobre connections.json).
    /// </summary>
    public sealed class ManageConnectionsCommand
    {
        private readonly AsyncPackage _package;
        private readonly OleMenuCommandService _commandService;
        private readonly ConnectionCatalog _catalog;

        public ManageConnectionsCommand(AsyncPackage package, OleMenuCommandService commandService, ConnectionCatalog catalog)
        {
            _package = package;
            _commandService = commandService;
            _catalog = catalog;
        }

        public void Register()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var commandId = new CommandID(PackageGuids.QuickToolsCmdSet, (int)PkgCmdId.ManageConnectionsCommand);
            _commandService.AddCommand(new OleMenuCommand(OnExecute, commandId));
        }

        private void OnExecute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // Sin duenio, ShowDialog no queda modal respecto de la ventana de SSMS y puede abrirse
            // detras; se usa el HWND del proceso (Ssms.exe) como ventana propietaria.
            var owner = MainWindowHandleWrapper.TryCreate();
            using (var dialog = new ManageConnectionsDialog(_catalog))
            {
                if (owner != null)
                {
                    dialog.ShowDialog(owner);
                }
                else
                {
                    dialog.ShowDialog();
                }
            }
        }

        private sealed class MainWindowHandleWrapper : IWin32Window
        {
            private MainWindowHandleWrapper(IntPtr handle)
            {
                Handle = handle;
            }

            public IntPtr Handle { get; }

            public static IWin32Window TryCreate()
            {
                var handle = Process.GetCurrentProcess().MainWindowHandle;
                return handle == IntPtr.Zero ? null : new MainWindowHandleWrapper(handle);
            }
        }
    }
}
