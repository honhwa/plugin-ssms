# Ensamblados de referencia — SSMS 22.6.11806.211

Copiados desde una instalación local de SSMS 22.6 (`Common7\IDE` y `Common7\IDE\Extensions\Application`),
solo para poder compilar (`CopyLocal=false`, no se redistribuyen en el `.vsix`).

No son de Microsoft para distribuir; **no commitear estos binarios a un repo público**. Si el repo se
publica, agregarlos a `.gitignore` y documentar en el README cómo regenerarlos (copiarlos de la propia
instalación de SSMS 22.6+).

Archivos:

- `Microsoft.SqlServer.Smo.dll`, `Microsoft.SqlServer.ConnectionInfo.dll`,
  `Microsoft.SqlServer.ConnectionInfoExtended.dll`, `Microsoft.SqlServer.Management.Sdk.Sfc.dll`
  — SMO, para scripting de tablas (Milestone 3).
- `Microsoft.SqlServer.GridControl.dll` — contiene `Microsoft.SqlServer.Management.UI.Grid.GridControl`
  e `IGridStorage`, usados por reflection en `GridReader` (Milestone 2).
- `SQLEditors.dll` (de `Extensions\Application`) — contiene
  `Microsoft.SqlServer.Management.UI.VSIntegration.Editors.ScriptFactory` / `IScriptFactory` y
  `Microsoft.SqlServer.Management.UI.VSIntegration.ServiceCache`, usados para conexión activa y
  creación de query windows (Milestones 1 y 3).
- `SqlWorkbench.Interfaces.dll` — `UIConnectionInfo` y tipos relacionados.
- `Microsoft.Data.SqlClient.dll` — `SqlConnection`/`SqlConnectionStringBuilder`, usados en
  `Ssms/SsmsHost.cs`, `Features/ScriptObject/ObjectScripter.cs` y
  `Features/QuickConnect/QuickConnectCommands.cs`. Referenciada acá (no como `PackageReference`)
  para no empaquetar en el `.vsix` un árbol de dependencias transitivas (Azure.Core,
  Azure.Identity, MSAL, System.Text.Json) más viejo que el que SSMS ya carga para otras
  extensiones — un solo AppDomain de .NET Framework resuelve por identidad de assembly, así que
  una copia vieja en la carpeta de esta extensión podría ganarle a la nueva que espera otro
  componente de SSMS. El `.vsix` tampoco traía las `Microsoft.Data.SqlClient.SNI.*.dll` nativas,
  así que la copia empaquetada nunca fue funcional por sí sola: en la práctica ya se dependía de
  la de SSMS.

Ruta origen (instalación de referencia): `C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\`.
