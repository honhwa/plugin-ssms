# 03 - Quick Connections: SQL Server Authentication + administrador

**Estado:** Implementado (verificación manual pendiente)
**Depende de:** docs/PLAN.md Milestone 1 (Quick Connections)
**Fecha:** 2026-10-24

**Objetivo:** que una conexión rápida pueda usar SQL Server Authentication (usuario/password además de
Windows), y dar un administrador dentro de SSMS para crear/editar/eliminar conexiones sin editar
`connections.json` a mano.

## Alcance

**Incluido:**
- Campo `authentication` por entrada: `windows` (default, compatible con archivos existentes) o `sql`.
- Campos `user` y `password` para las entradas `sql`.
- Password cifrado en disco con DPAPI (`DataProtectionScope.CurrentUser`), con prefijo `dpapi:` para
  distinguirlo de un valor escrito a mano.
- Si el password no se puede descifrar (archivo copiado a otro usuario/máquina) o nunca se guardó, se pide
  al conectar, con opción de guardarlo (cifrado) en ese momento.
- Comando **Quick Tools → Manage Quick Connections...**: lista + formulario con Nueva / Guardar / Eliminar /
  Probar conexión / Cerrar.
- Reescritura del archivo completa y atómica en cada alta/edición/baja, manteniendo el formato de un objeto
  por línea (el archivo sigue siendo cómodo de editar a mano).
- Corrección de `SsmsHost.TryReconnectActiveWindow`: dos entradas al mismo servidor con **usuario SQL
  distinto** ya no se consideran "misma conexión" (antes solo cambiaba `CurrentDB` y quedaban las
  credenciales viejas).

**No incluido (fuera de este spec):**
- Microsoft Entra ID (antes Azure AD) y sus variantes: `BuildConnectionString` sigue cayendo a Windows como
  mejor esfuerzo. Requiere otro flujo de autenticación y no hay forma de probarlo acá.
- Guardar credenciales en el almacén de SSMS/VS o en el Administrador de credenciales de Windows.
- Sincronizar el archivo entre máquinas. DPAPI es deliberadamente local: una copia del archivo en otro equipo
  pierde los passwords (se vuelven a pedir).
- Rotación/expiración de passwords, o detección de password vencido.
- Importar/exportar conexiones, o reordenarlas por drag & drop.

## Modelo de datos

```json
{
  "connections": [
    { "name": "DEV.Figuritas", "server": "srv\\DEV01", "database": "Figuritas", "authentication": "windows" },
    { "name": "PROD.Ventas", "server": "sql-prod01", "database": "Ventas",
      "authentication": "sql", "user": "app_ventas", "password": "dpapi:AQAAANCMnd8BFdERjHo..." }
  ]
}
```

Reglas de lectura (`ConnectionsJson.Parse`):

| Caso | Resultado |
| --- | --- |
| Sin `authentication`, o `windows`/`integrated`/desconocido | Windows; `user`/`password` se descartan |
| `sql` con `user` no vacío | SQL Server; `password` se descifra |
| `sql` sin `user` | se degrada a Windows (evita ofrecer una conexión que siempre va a fallar) |
| `name`/`server`/`database` vacíos | la entrada se descarta |
| `name` duplicado (case-insensitive) | se queda la primera |
| `password` sin prefijo `dpapi:` | se usa tal cual (permite escribirlo a mano) |
| `password` `dpapi:` que no descifra | queda `null` → `NeedsPassword` |

Reglas de escritura (`ConnectionsJson.Serialize`): `authentication` siempre explícito, `user`/`password` solo
para `sql`, `password` solo si hay valor. Un objeto por línea, sangría de 2 espacios, UTF-8 sin BOM,
finales de línea CRLF.

## Cifrado

`SecretProtector` (DPAPI, CurrentUser) + prefijo `dpapi:`. Al descifrar, `FormatException` y
`CryptographicException` se traducen a `null`: un archivo copiado desde otra cuenta no rompe la carga del
catálogo, solo hace que se pida el password. La UI avisa cuando una entrada quedó sin password utilizable.

## UI

`ManageConnectionsDialog`, modal, abierto desde el comando (con el HWND de `Ssms.exe` como owner para que no
quede detrás de SSMS):

- Izquierda: lista (nombre + `(SQL: usuario)` cuando aplica), botones **Nueva** / **Eliminar**.
- Derecha: Nombre, Servidor, Base de datos, Autenticación (Windows/SQL Server), Usuario, Password
  (enmascarado + "Mostrar"). Usuario/Password se deshabilitan con Windows auth.
- Abajo: **Probar conexión**, **Guardar**, **Cerrar**, y el path del archivo como texto de estado.
- Guardar: valida nombre/servidor/base/usuario, rechaza nombres duplicados (salvo que sea la misma entrada),
  soporta renombrar (borra el nombre viejo + inserta el nuevo en la misma reescritura del archivo).
- Probar conexión: abre y cierra una conexión ADO.NET con timeout de 5 s usando exactamente la misma
  política que la conexión real (Optional + validar certificado). No toca ninguna ventana de SSMS.
- Eliminar: confirma (default = No) y reescribe el archivo.

`PasswordPromptDialog` (se abre al conectar): muestra nombre/servidor/base/usuario, password enmascarado,
checkbox "Guardar el password (cifrado con DPAPI para este usuario de Windows)" marcado por defecto.
Cancelar = no se intenta conectar.

## Flujo de conexión

1. `QuickConnectCommands` toma la entrada del catálogo (copia).
2. Si `entry.NeedsPassword`, pide password; si el usuario tildó guardar, `catalog.AddOrUpdate(entry)` lo
   persiste cifrado.
3. `QuickConnectionFactory.CreateConnectionInfo` arma el `UIConnectionInfo`: `AuthenticationType` 0/1,
   `UserName`/`Password` para SQL, `ServerType` del Database Engine y `AdvancedOptions`
   (`DATABASE`, `ENCRYPT_CONNECTION=Optional`, `TRUST_SERVER_CERTIFICATE=False`).
4. `SsmsHost.TryReconnectActiveWindow` decide entre cambiar `CurrentDB` (misma conexión lógica) o reconectar;
   ahora el usuario SQL forma parte de esa comparación.
5. Sin ventana de query activa, se abre una nueva con una conexión ADO.NET de `QuickConnectionFactory.Open`.

## Archivos

- Nuevos: `Features/QuickConnect/ConnectionEntry.cs`, `ConnectionsJson.cs`, `SecretProtector.cs`,
  `QuickConnectionFactory.cs`, `ManageConnectionsDialog.cs`, `ManageConnectionsCommand.cs`,
  `PasswordPromptDialog.cs`.
- Modificados: `ConnectionCatalog.cs` (CRUD + guardado atómico + parseo delegado), `QuickConnectCommands.cs`
  (password + factory), `Ssms/SsmsHost.cs` (comparación de usuario), `SsmsQuickTools.vsct` +
  `Guids.cs` (`cmdidManageConnectionsCommand`), `SsmsQuickToolsPackage.cs` (registro),
  `Resources/connections.example.json`, `SsmsQuickTools.csproj` (`System.Security` para DPAPI).

## Tests

Unitarios (`SsmsQuickTools.Tests/ConnectionsJsonTests.cs`, compilan los .cs de lógica pura por link, sin
arrastrar SSMS): parseo de archivos viejos sin `authentication`, `sql` con password cifrado, password
indescifrable, `sql` sin usuario degradado, `authentication` case-insensitive y desconocida, entradas
incompletas, nombres duplicados, BOM, JSON inválido, serializado (omite `user`/`password` en Windows, cifra
en SQL, escapa comillas/backslash, un objeto por línea, round-trip completo), y DPAPI
(`Protect`/`Unprotect`, vacíos, base64 inválido, blob ajeno, texto plano).

## Verificación manual (pendiente, requiere SSMS real)

1. Quick Tools → Manage Quick Connections... aparece; el diálogo abre modal sobre SSMS y centrado.
2. Nueva entrada SQL: guardar, cerrar SSMS, reabrir → aparece en el combo y conecta sin pedir password.
3. `connections.json` en disco: el `password` empieza con `dpapi:` y no contiene el texto en claro.
4. Borrar el campo `password` de una entrada SQL y reconectar → pide password; con "guardar" marcado, el
   archivo se reescribe cifrado.
5. Copiar `connections.json` a otra cuenta de Windows (o máquina) → la entrada carga, se pide password, y el
   resto del catálogo sigue funcionando.
6. Probar conexión con password incorrecto → mensaje con el error de SQL, sin abrir ventanas.
7. Mismo servidor, dos entradas SQL con usuarios distintos → al cambiar de una a otra reconecta (no solo
   cambia la base) y `SELECT SUSER_SNAME()` muestra el usuario correcto.
8. Windows auth sigue funcionando igual que antes (archivo viejo, sin `authentication`).
