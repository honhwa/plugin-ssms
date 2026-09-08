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

Ruta origen (instalación de referencia): `C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\`.
