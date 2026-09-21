using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace SsmsQuickTools.Features.About
{
    /// <summary>
    /// Comando "About" en el menu Quick Tools: muestra version y fecha de build (BuildInfo.g.cs,
    /// generado en SsmsQuickTools.csproj a partir de source.extension.vsixmanifest) y el autor.
    /// Util para saber que .vsix esta instalado durante el ciclo de pruebas manual contra SSMS.
    /// </summary>
    public sealed class AboutCommand
    {
        private readonly AsyncPackage _package;
        private readonly OleMenuCommandService _commandService;

        public AboutCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _package = package;
            _commandService = commandService;
        }

        public void Register()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var commandId = new CommandID(PackageGuids.QuickToolsCmdSet, (int)PkgCmdId.AboutCommand);
            var command = new OleMenuCommand(OnAbout, commandId);
            _commandService.AddCommand(command);
        }

        private void OnAbout(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var text = "Build version: " + BuildInfo.Version + Environment.NewLine
                     + "Build date: " + BuildInfo.Date + Environment.NewLine
                     + "Autor: Jose Blando";

            VsShellUtilities.ShowMessageBox(
                _package,
                text,
                "SsmsQuickTools",
                OLEMSGICON.OLEMSGICON_INFO,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }
}
