namespace SsmsQuickTools.Features.QuickConnect
{
    /// <summary>
    /// Tipo de autenticacion de una conexion rapida. Los valores coinciden con los de
    /// <c>UIConnectionInfo.AuthenticationType</c> (0 = Windows, 1 = SQL Server).
    /// </summary>
    public enum ConnectionAuthentication
    {
        Windows = 0,
        SqlServer = 1,
    }

    /// <summary>
    /// Una conexion rapida: servidor + base + como autenticarse.
    /// <see cref="Password"/> esta en claro solo en memoria; en disco se guarda cifrada con DPAPI
    /// (ver <see cref="SecretProtector"/>). Queda en null cuando no hay password guardada o cuando
    /// no se pudo descifrar (archivo copiado a otro usuario/maquina).
    /// </summary>
    public sealed class ConnectionEntry
    {
        public string Name { get; set; }
        public string Server { get; set; }
        public string Database { get; set; }
        public ConnectionAuthentication Authentication { get; set; } = ConnectionAuthentication.Windows;
        public string UserName { get; set; }
        public string Password { get; set; }

        /// <summary>
        /// True cuando la entrada usa SQL Server Authentication y no hay password utilizable: hay
        /// que pedirla (y opcionalmente guardarla) antes de conectar.
        /// </summary>
        public bool NeedsPassword =>
            Authentication == ConnectionAuthentication.SqlServer && string.IsNullOrEmpty(Password);

        public ConnectionEntry Clone()
        {
            return new ConnectionEntry
            {
                Name = Name,
                Server = Server,
                Database = Database,
                Authentication = Authentication,
                UserName = UserName,
                Password = Password,
            };
        }
    }
}
