using System;
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management.Smo.RegSvrEnum;
using SsmsQuickTools.Ssms;

namespace SsmsQuickTools.Features.QuickConnect
{
    /// <summary>
    /// Traduce una <see cref="ConnectionEntry"/> al par que necesita SSMS: un
    /// <see cref="UIConnectionInfo"/> (para reconectar la ventana) y una conexion ADO.NET (para las
    /// reconexiones completas y para "Test connection"). Unico lugar donde se decide como se
    /// autentica y con que politica de cifrado/certificado, para que el test del administrador y la
    /// conexion real no puedan divergir.
    /// </summary>
    internal static class QuickConnectionFactory
    {
        // Seguridad de las conexiones de Quick Connect: sin cifrado obligatorio y validando el
        // certificado del servidor (equivale a los defaults "Optional"/sin marcar del dialogo de SSMS).
        public static readonly SqlConnectionEncryptOption EncryptOption = SqlConnectionEncryptOption.Optional;
        public const bool TrustServerCertificate = false;

        /// <summary>Timeout corto para "Test connection": no dejar la UI colgada 15s por un host muerto.</summary>
        private const int TestConnectTimeoutSeconds = 5;

        // Database Engine: sin este GUID, VerifyConnectionInfo (ConnectionDlg.dll) rechaza la
        // conexion con "Tipo de conexion inesperado".
        private static readonly Guid DatabaseEngineServerType = new Guid("8c91a03d-f9b4-46c0-a305-b5dcc79ff907");

        public static UIConnectionInfo CreateConnectionInfo(ConnectionEntry entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            var isSql = entry.Authentication == ConnectionAuthentication.SqlServer;

            var connectionInfo = new UIConnectionInfo
            {
                ServerName = entry.Server,
                // 0 = Windows Authentication, 1 = SQL Server Authentication.
                AuthenticationType = isSql ? 1 : 0,
                ApplicationName = "SsmsQuickTools",
                ServerType = DatabaseEngineServerType,
            };

            if (isSql)
            {
                connectionInfo.UserName = entry.UserName;
                connectionInfo.Password = entry.Password ?? string.Empty;
            }

            connectionInfo.AdvancedOptions["DATABASE"] = entry.Database;
            // SSMS arma sus propias conexiones (scripting, etc.) a partir de estas claves
            // (UIConnectionInfoUtil en ConnectionDlg.dll); sin ellas cae a Mandatory + sin confiar
            // en el certificado y falla con "cadena de certificacion ... no se confia".
            connectionInfo.AdvancedOptions["ENCRYPT_CONNECTION"] = EncryptOption.ToString();
            connectionInfo.AdvancedOptions["TRUST_SERVER_CERTIFICATE"] = TrustServerCertificate.ToString();

            return connectionInfo;
        }

        /// <summary>
        /// Abre una conexion ADO.NET nueva con la misma politica que la conexion real. El llamador
        /// queda como duenio y debe disponerla.
        /// </summary>
        public static IDbConnection Open(UIConnectionInfo connectionInfo, string database)
        {
            var dbConnection = new SqlConnection(BuildConnectionString(connectionInfo, database));
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

        /// <summary>
        /// Prueba la conexion. Tira si falla (el llamador muestra el mensaje); no toca ninguna
        /// ventana de SSMS ni deja conexiones abiertas.
        /// </summary>
        public static void Test(ConnectionEntry entry)
        {
            var connectionInfo = CreateConnectionInfo(entry);
            var builder = new SqlConnectionStringBuilder(BuildConnectionString(connectionInfo, entry.Database))
            {
                ConnectTimeout = TestConnectTimeoutSeconds,
            };

            using (var dbConnection = new SqlConnection(builder.ConnectionString))
            {
                dbConnection.Open();
            }
        }

        private static string BuildConnectionString(UIConnectionInfo connectionInfo, string database)
        {
            // Microsoft.Data.SqlClient usa Encrypt=Mandatory por defecto y SsmsHost.BuildConnectionString
            // confia siempre en el certificado; aca se impone la politica de Quick Connect (Optional +
            // validacion del certificado), la misma que quedo en connectionInfo.AdvancedOptions.
            var builder = new SqlConnectionStringBuilder(SsmsHost.BuildConnectionString(connectionInfo, database))
            {
                Encrypt = EncryptOption,
                TrustServerCertificate = TrustServerCertificate,
            };
            return builder.ConnectionString;
        }
    }
}
