using System;
using System.Security.Cryptography;
using System.Text;

namespace SsmsQuickTools.Features.QuickConnect
{
    /// <summary>
    /// Cifra/descifra el password de las conexiones SQL con DPAPI (ambito CurrentUser): solo la
    /// cuenta de Windows que lo escribio puede descifrarlo, en esa maquina. Si el archivo se copia
    /// a otra cuenta o a otra maquina, <see cref="Unprotect"/> devuelve null y el password se pide
    /// de nuevo (no se rompe la carga del catalogo).
    /// </summary>
    internal static class SecretProtector
    {
        /// <summary>Marca que distingue un valor cifrado por nosotros de uno escrito a mano.</summary>
        public const string Prefix = "dpapi:";

        public static string Protect(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
            {
                return null;
            }

            var cipher = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(plainText),
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser);
            return Prefix + Convert.ToBase64String(cipher);
        }

        /// <summary>
        /// Devuelve el password en claro, o null si el valor esta vacio o no se puede descifrar.
        /// Un valor sin <see cref="Prefix"/> se devuelve tal cual: permite escribir el password a
        /// mano en connections.json (al proximo guardado desde la UI queda cifrado).
        /// </summary>
        public static string Unprotect(string storedValue)
        {
            if (string.IsNullOrEmpty(storedValue))
            {
                return null;
            }

            if (!storedValue.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return storedValue;
            }

            try
            {
                var cipher = Convert.FromBase64String(storedValue.Substring(Prefix.Length));
                var plain = ProtectedData.Unprotect(cipher, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch (FormatException)
            {
                return null;
            }
            catch (CryptographicException)
            {
                // Guardado por otro usuario/maquina, o el blob se corrompio.
                return null;
            }
        }
    }
}
