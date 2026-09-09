# Plugin SSMS 22 — Quick Connections, Script desde Grid, Generar ALTER/CREATE

## Contexto

El usuario quiere una extensión propia para SQL Server Management Studio 22.6.0 (y posteriores) con tres funcionalidades de productividad que hoy requieren pasos manuales repetitivos:

1. **Quick Connections**: un combo en la barra de herramientas para reconectar la ventana de query activa a un servidor/base de datos definidos en un archivo de configuración.
2. **Grid → script SELECT**: tomar el resultado visible de una consulta y dejar en el portapapeles un script autocontenido que reproduce esos datos al pegarlo y ejecutarlo.
3. **Generar ALTER / Generar CREATE**: al seleccionar el nombre de un objeto en el editor, ofrecer en el menú contextual la generación del script correspondiente.

El directorio de trabajo (`C:\Users\jobla\Desktop\ClaudeCode\plugin-ssms`) está vacío: es un proyecto desde cero.

## Viabilidad — resumen

Es **viable**, con dificultad media-alta. SSMS 21+ está construido sobre el shell de Visual Studio 2022, es 64-bit, y acepta instalación de archivos `.vsix`. Confirmado por extensiones vivas que ya soportan SSMS 21/22 (`SqlCeToolbox`, `SSMS-Schema-Folders`).

Advertencias que deben quedar claras antes de empezar:

- Microsoft **no da soporte oficial** a extensiones de terceros en SSMS 21+. Funciona, pero cualquier feedback a Microsoft será cerrado. Una actualización de SSMS puede romper la extensión.
- Las funcionalidades 1 y 3 usan APIs internas de SSMS (`SQLEditors.dll`, `SqlWorkbench.Interfaces.dll`) documentadas solo parcialmente y a través de referencias directas a los DLL instalados.
- La funcionalidad 2, con el enfoque elegido (**leer el grid por reflection**), depende de tipos internos no documentados (`Microsoft.SqlServer.Management.UI.Grid.GridControl`, `IGridStorage`). Es la parte más frágil del proyecto y la que más probablemente se rompa entre versiones. Se mitiga aislándola tras una interfaz con un fallback por portapapeles (TSV).
- El grid solo expone los valores **como texto**, no los tipos SQL. Los tipos del script generado se **infieren** por heurística sobre el texto. Aceptable para pegar y ejecutar; no es fiel al esquema original.

Esfuerzo estimado: M0+M1 en un par de sesiones; M2 es el riesgo real (iteración con SSMS abierto); M3 es directo.

## Decisiones tomadas

| Tema | Decisión |
|---|---|
| Origen de datos para el SELECT | Leer el grid de resultados por reflection |
| Formato del script | `WITH ... AS (SELECT * FROM (VALUES (...),(...)) v(cols)) SELECT * FROM ...` |
| Quick Connections | Reconectar la ventana de query activa (cambio de conexión + `USE database`) |
| Autenticación en el archivo | Solo Windows / Integrated (sin credenciales en el archivo) |

## Stack y configuración del proyecto

- Visual Studio 2022 (17.14+) con workload **Visual Studio extension development**.
- Plantilla **VSIX Project (VSPackage)**, formato VSIX v3.
- Target: **.NET Framework 4.8**, `PlatformTarget=AnyCPU`, `<Prefer32Bit>false</Prefer32Bit>` (necesario para que cargue también en Windows ARM64, donde el CLR es ARM64 nativo).
- Referencias a DLL de SSMS desde `C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\`, todas con `CopyLocal=false`:
  - `SQLEditors.dll` — `ServiceCache`, `IScriptFactory`, `UIConnectionInfo`
  - `SqlWorkbench.Interfaces.dll`
  - `Microsoft.SqlServer.Management.UI.Grid.dll` (o el ensamblado que contenga `GridControl` en la versión instalada; verificar en la instalación real)

**Versión mínima soportada: SSMS 22.6.0.** Referenciar los DLL de SSMS de la versión **más baja** soportada (22.6.0), no de la más alta: las referencias de .NET Framework se enlazan por nombre fuerte y una referencia a un ensamblado más nuevo no cargará sobre una instalación 22.6.0. Si la máquina de desarrollo tiene una versión superior, copiar los DLL de 22.6.0 a `lib\ssms22.6\` en el repositorio y referenciarlos desde ahí (`HintPath`, `CopyLocal=false`, `SpecificVersion=false`).

Además, `SpecificVersion=False` en todas las referencias a DLL de SSMS, para tolerar los incrementos de versión entre 22.6 y las siguientes.
  - `Microsoft.SqlServer.Smo.dll`, `Microsoft.SqlServer.ConnectionInfo.dll`, `Microsoft.SqlServer.Management.Sdk.Sfc.dll` (vía NuGet `Microsoft.SqlServer.SqlManagementObjects` si la versión es compatible; si no, referenciar las de SSMS)
- `source.extension.vsixmanifest`:

```xml
<Installation>
  <InstallationTarget Id="Microsoft.VisualStudio.Ssms" Version="[22.6,24.0)">
    <ProductArchitecture>amd64</ProductArchitecture>
  </InstallationTarget>
</Installation>
<Prerequisites>
  <Prerequisite Id="Microsoft.VisualStudio.Component.CoreEditor" Version="[17.0,)" DisplayName="Visual Studio core editor" />
</Prerequisites>
```

- Depuración: propiedades del proyecto → Debug → programa externo `Ssms.exe` con argumento `/rootsuffix Exp` si el hive experimental existe; si no, instalar el `.vsix` generado y adjuntar el depurador al proceso `Ssms.exe`.
- Las extensiones se instalan en `%LocalAppData%\Microsoft\SSMS\<version_id>\Extensions\` (ej. `22.0_8a60b991`). Útil para limpiar instalaciones fallidas.

## Estructura propuesta

```
plugin-ssms/
  SsmsQuickTools.sln
  SsmsQuickTools/
    SsmsQuickTools.csproj
    source.extension.vsixmanifest
    SsmsQuickToolsPackage.cs        // AsyncPackage, InitializeAsync, registro de comandos
    SsmsQuickTools.vsct             // toolbar, combos y menú contextual
    Ssms/
      SsmsHost.cs                   // acceso a ServiceCache, conexión activa, ventana activa
      GridReader.cs                 // reflection sobre GridControl  (aislado)
      ClipboardGridReader.cs        // fallback TSV
      IResultSetReader.cs           // interfaz común: columnas + filas de string
    Features/
      QuickConnect/
        ConnectionCatalog.cs        // carga/watch del archivo de configuración
        QuickConnectCommands.cs     // handlers de los dos combos
      ScriptData/
        ValuesScriptBuilder.cs      // inferencia de tipos + generación del CTE
        ScriptDataCommand.cs
      ScriptObject/
        ObjectNameParser.cs         // parseo de [db].[schema].[obj] desde el texto seleccionado
        ObjectScripter.cs           // SMO / OBJECT_DEFINITION
        ScriptObjectCommands.cs
  README.md
```

## Milestone 0 — Esqueleto que carga

1. Crear la solución y el proyecto VSIX con `AsyncPackage` y `ProvideAutoLoad(UIContextGuids80.NoSolution / EmptySolution)` — en SSMS nunca hay solución abierta, así que la carga debe dispararse por contexto de UI o por el propio comando.
2. Añadir un comando de prueba en el menú **Tools** que muestre un `MessageBox`.
3. Compilar, instalar el `.vsix` en SSMS 22.6.0 y verificar que aparece.

**Este hito valida el 80% del riesgo de plataforma.** No avanzar sin él.

## Milestone 1 — Quick Connections

Archivo de configuración en `%APPDATA%\SsmsQuickTools\connections.json`, recargado con `FileSystemWatcher`:

```json
{
  "servers": [
    {
      "name": "PROD",
      "server": "sql-prod01\\INST1",
      "databases": ["Ventas", "Facturacion"]
    },
    { "name": "DEV", "server": "localhost", "databases": ["VentasDev"] }
  ]
}
```

Solo autenticación integrada; no se almacenan credenciales.

En el `.vsct`, dos combos en una toolbar propia:

```xml
<Combo guid="guidSsmsQuickTools" id="cmdidServerCombo" priority="0x0100"
       type="DropDownCombo" idCommandList="cmdidServerComboGetList"
       defaultWidth="180">
```

- Un handler responde con la lista (`OleMenuCmdEventArgs.OutValue` = `string[]`) y otro con la selección.
- El combo de bases se repuebla al cambiar el de servidores.
- Al seleccionar base: obtener la conexión activa con `ServiceCache.ScriptFactory.CurrentlyActiveWndConnectionInfo` y cambiar la conexión de la ventana activa. Si cambiar la conexión in-place resulta inestable, el fallback es `ServiceCache.ScriptFactory.CreateNewBlankScript(ScriptType.Sql, connectionInfo, null)` — decidir al probar.

## Milestone 2 — Grid → script SELECT (parte de mayor riesgo)

Definir `IResultSetReader` que devuelve `(string[] columnas, IReadOnlyList<string[]> filas)`, con dos implementaciones:

**A. `GridReader` (principal, reflection).** Pasos:
1. Frame activo vía `IVsMonitorSelection` / `IVsUIShell.GetDocumentWindowEnum`; obtener el HWND con `__VSFPROPID.VSFPROPID_ParentHwnd`.
2. `Control.FromChildHandle(hwnd)` y recorrer el árbol de controles WinForms buscando el tipo cuyo nombre completo termine en `.GridControl`.
3. Sobre esa instancia, por reflection: `GridStorage` (`IGridStorage`) → `NumRows()` y `GetCellDataAsString(row, col)`; columnas desde `GetColumnInfo(...)` / la colección interna de columnas.
4. Si hay selección de celdas, limitarse a ese rango; si no, tomar el grid completo.
5. Todo el acceso por reflection va con nombres en constantes y `try/catch`; ante cualquier fallo se registra el motivo y se ofrece el fallback.

**B. `ClipboardGridReader` (fallback).** Parsea TSV del portapapeles (con la opción "incluir encabezados" de SSMS). Se usa automáticamente si A falla, con un aviso al usuario.

**Generación del script** (`ValuesScriptBuilder`): inferencia de tipo por columna, evaluando todos los valores no nulos y quedándose con el tipo más restrictivo que los acepte a todos: `bit` → `int` → `bigint` → `decimal(p,s)` → `uniqueidentifier` → `datetime2` → `nvarchar(n)` (fallback). `NULL` textual se mapea a `NULL` real. Escapado de comillas simples, prefijo `N'` para texto.

Salida en portapapeles:

```sql
WITH datos (Id, Nombre, Fecha) AS (
    SELECT * FROM (VALUES
        (1, N'Ana', '2026-01-15'),
        (2, N'Luis', '2026-02-01')
    ) v (Id, Nombre, Fecha)
)
SELECT * FROM datos;
```

Sin `CAST` explícito: literales simples (números, `NULL`) y con prefijo `N'...'`/comillas para texto, fecha y GUID; el tipo de columna lo infiere el motor a partir de todos los literales de la tabla derivada. Límite configurable de filas (por defecto 1000) con confirmación si se supera. `VALUES` admite hasta 1000 filas por constructor: por encima de eso, dividir en varios `SELECT ... UNION ALL` de bloques de 1000.

**Corrección verificada contra el motor (`SsmsQuickTools.Tests/ScriptRoundTripTests.cs`):** la inferencia de tipo del motor solo aplica a literales numéricos sin comillas (`int`/`bigint`/`decimal`/`bit`). Un literal entre comillas simples (`datetime2`, `uniqueidentifier`) **no se convierte** — nada en `SELECT * FROM datos` fuerza esa conversión, así que la columna vuelve como `varchar`/`nvarchar`, no como fecha o GUID reales. Confirmado con `SQL_VARIANT_PROPERTY(..., 'BaseType')` contra `LENOVOJOSE\DEV01`. El valor pegado y re-ejecutado se ve igual en el grid, pero downstream (`WHERE Fecha > @p datetime2`, `INSERT INTO` una columna tipada) puede requerir conversión implícita/explícita que antes el `CAST` daba gratis. Aceptado como limitación conocida por ahora; no bloquea M2.

Comando expuesto en el menú contextual del grid de resultados y en **Tools**, con atajo de teclado.

## Milestone 3 — Generar ALTER / Generar CREATE

1. Menú contextual del editor: grupo bajo `IDM_VS_CTXT_CODEWIN` con dos botones, "Generar CREATE" y "Generar ALTER".
2. Texto seleccionado vía `IVsTextManager.GetActiveView` → `IVsTextView.GetSelectedText()`; si no hay selección, tomar la palabra bajo el cursor.
3. `ObjectNameParser` normaliza `[db].[schema].[obj]`, `schema.obj` u `obj` (con `dbo` y la base actual como defaults).
4. `ObjectScripter`, sobre la conexión activa:
   - Objetos programables (procedimiento, vista, función, trigger): `OBJECT_DEFINITION(OBJECT_ID(@name))`. Para ALTER, reemplazar el primer `CREATE` por `ALTER` con una expresión regular anclada al inicio del cuerpo (respeta comentarios previos).
   - Tablas: SMO `Scripter` con `ScriptingOptions { ScriptDrops = false, Indexes = true, DriAll = true, SchemaQualify = true }`. **No existe ALTER para una tabla**: en ese caso "Generar ALTER" se deshabilita y se avisa.
   - `IVsMonitorSelection` / `OleMenuCommand.BeforeQueryStatus` decide visibilidad y habilitación según lo que exista en el servidor (una consulta a `sys.objects` para resolver el tipo).
5. Resultado: nueva ventana de query con el script (`ScriptFactory.CreateNewBlankScript` + inserción de texto), y copia al portapapeles.

## Verificación

Todo se verifica contra SSMS real; no hay pruebas automatizadas de la capa de UI. Verificar como mínimo en **22.6.0** (piso soportado) y en la versión más reciente disponible, ya que el `GridReader` por reflection es lo que más probablemente difiera entre ambas.

- **M0**: la extensión aparece en Extensions → Manage Extensions y el comando de prueba responde.
- **M1**: editar `connections.json` con dos servidores; los combos se pueblan; seleccionar una base cambia la conexión de la ventana activa (verificar con `SELECT @@SERVERNAME, DB_NAME()`).
- **M2**: ejecutar una consulta con columnas de tipos mixtos (int, nvarchar con comilla simple, datetime, NULL, decimal, uniqueidentifier, bit); usar el comando; pegar el resultado en una ventana nueva y confirmar que ejecuta y devuelve las mismas filas. Repetir con selección parcial de celdas y con más de 1000 filas.
  **Hecho** (2026-09-09, contra `LENOVOJOSE\DEV01`/`Figuritas`): checklist manual completo (comando por Tools y por Ctrl+Shift+D, selección parcial de filas, dos result sets, >1000 filas con confirmación, casos de error). Los TSV capturados quedaron como fixtures reales en `SsmsQuickTools.Tests/Fixtures/` (`tipos_mixtos.tsv`, `tipos_mixtos_seleccion_parcial.tsv`, `volumen_1500filas.tsv`) y se ejecutan automáticamente en `ScriptRoundTripTests.cs` cuando `SSMSQT_TEST_CONNECTION` está definida (esas capturas se hicieron sin encabezado a propósito, así que se les agregó a mano el header conocido de cada consulta antes de usarlas como fixture). Pendiente todavía: el fallback de `ClipboardTsvReader` con "Include column headers when copying or saving results" desactivado en Tools → Options — sin encabezado real, toma la primera fila de datos como encabezado y la pierde en silencio; ese caso concreto (deliberado, "qué pasa si me olvido la opción") no se ejercitó todavía en la UI. Tampoco se confirmó selección parcial de *columnas* (la captura recibida trajo filas completas), solo de filas.
- **M3**: probar sobre una tabla, una vista, un procedimiento y una función; con nombre completo y con nombre simple; y con un objeto inexistente (debe avisar sin excepción).
- Prueba de regresión de riesgo: reiniciar SSMS varias veces y confirmar que no se degrada el arranque ni aparecen errores en `%AppData%\Microsoft\SSMS\ActivityLog.xml` (arrancar con `Ssms.exe /log` para generarlo).

## Unit tests

`ValuesScriptBuilder` y `ObjectNameParser` son lógica pura sin dependencias de SSMS: proyecto de tests separado con xUnit cubriendo inferencia de tipos, escapado, particionado en bloques de 1000 y parseo de nombres.
