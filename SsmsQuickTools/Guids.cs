using System;

namespace SsmsQuickTools
{
    /// <summary>
    /// GUIDs del paquete y de los command sets, compartidos entre el codigo administrado
    /// y el archivo .vsct. Deben coincidir exactamente con las constantes definidas alli.
    /// </summary>
    internal static class PackageGuids
    {
        public const string PackageString = "016d6f06-3db3-4bd2-a0b7-c19e4d1a3232";
        public static readonly Guid Package = new Guid(PackageString);

        // Comandos generales (Tools menu, toolbar Quick Connect)
        public const string QuickToolsCmdSetString = "2e7f3eab-6748-4aa6-bdcf-9c69e43a48fd";
        public static readonly Guid QuickToolsCmdSet = new Guid(QuickToolsCmdSetString);

        // Menu contextual del editor de texto (Generar CREATE / Generar ALTER)
        public const string EditorContextCmdSetString = "d71b3e2f-929e-4f15-9652-e299c6a4b288";
        public static readonly Guid EditorContextCmdSet = new Guid(EditorContextCmdSetString);
    }

    internal static class PkgCmdId
    {
        // Toolbar Quick Connect
        public const uint QuickConnectToolbar = 0x1000;
        public const uint QuickConnectToolbarGroup = 0x1001;

        public const uint ServerCombo = 0x0100;
        public const uint ServerComboGetList = 0x0101;
        public const uint DatabaseCombo = 0x0102;
        public const uint DatabaseComboGetList = 0x0103;

        // Comando "Script de datos del grid" (Tools + menu contextual del grid)
        public const uint ScriptDataCommand = 0x0200;

        // Menu contextual del editor: Generar CREATE / Generar ALTER
        public const uint EditorContextGroup = 0x2001;
        public const uint GenerateCreateCommand = 0x0300;
        public const uint GenerateAlterCommand = 0x0301;
    }
}
