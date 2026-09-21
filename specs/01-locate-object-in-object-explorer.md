# 01 - Locate Object in Object Explorer

**Estado:** Approved
**Depende de:** ninguno
**Fecha:** 2026-09-20

**Objetivo:** agregar un comando "Locate in Object Explorer" al submenu Quick Tools > Query que ubica y selecciona, en el arbol de Object Explorer, el objeto (tabla/vista/procedimiento/funcion/trigger) cuyo nombre esta seleccionado o bajo el cursor en la query activa.

## Alcance

**Incluido:**
- Comando nuevo en el submenu `Query` de Quick Tools (mismo grupo que "Script Object as CREATE/ALTER"), con atajo `Ctrl+K, Ctrl+6`.
- Reusa el mismo origen de texto que Script CREATE/ALTER: seleccion activa o palabra bajo el cursor (`SsmsHost.GetActiveSelectedText` / `GetWordUnderCursor`), parseado con `ObjectNameParser` existente.
- Tipos de objeto soportados: los mismos que Script CREATE/ALTER — tabla, vista, procedimiento, funcion, trigger.
- Busca el nodo del objeto dentro del arbol de Object Explorer ya conectado a la misma conexion (servidor + base de datos) que la ventana de query activa.
- Si las carpetas intermedias del arbol (`Databases > <db> > Tables`, etc.) no fueron expandidas todavia por el usuario, el comando fuerza su expansion/carga para poder buscar el objeto.
- Al encontrar el nodo: lo selecciona y le da foco (scroll-into-view) en el panel de Object Explorer.
- Si Object Explorer no tiene un nodo conectado a esa conexion/base de datos exacta (incluye el caso de un objeto calificado con una base de datos distinta, ej. `[OtraDB].[dbo].[Tabla]`), o si el objeto no aparece dentro del arbol ya expandido (no existe, sin permisos, o cualquier otro motivo), se trata como "no encontrado": `MessageBox.Show` con icono Warning, mismo patron que `ScriptObjectCommands`.

**No incluido (fuera de este spec):**
- Conectar automaticamente Object Explorer a una conexion/base de datos que no tiene un nodo abierto.
- Buscar el objeto en otras conexiones/servidores distintos al de la query activa.
- Soporte para tipos de objeto fuera del set de Script CREATE/ALTER (schemas, sinonimos, indices, columnas individuales, etc.).
- Abrir el panel de Object Explorer si esta cerrado (se asume visible; si no lo esta, cae en el mismo flujo de "no encontrado" al no poder localizar el arbol).

## Modelo de datos

No introduce estructuras de datos ni persistencia nueva. Reusa `ObjectNameParser`/`ParsedObjectName` ya existentes en `Features/ScriptObject/`.

## Plan de implementacion

1. **Guids.cs / SsmsQuickTools.vsct** — agregar `cmdidLocateObjectCommand` (`IDSymbol` nuevo, ej. `0x0203`, bajo `guidQuickToolsCmdSet`) como `Button` en `QueryMenuGroup`, icono via `ImageCatalogGuid` (ej. `GoToDefinition` o similar disponible), `KeyBinding` `Ctrl+K, Ctrl+6`. El sistema sigue compilando y el resto de comandos existentes no se ve afectado.
2. **Ssms/SsmsHost.cs** — agregar el/los metodo(s) para acceder al arbol de Object Explorer: obtener el nodo raiz de la conexion activa (servidor+base de datos), expandir/forzar carga de una carpeta por tipo de objeto (Tables/Views/Stored Procedures/Functions/Triggers de la tabla padre, para el caso de triggers), y buscar+seleccionar un nodo hijo por nombre (schema+objeto). Codigo aislado en este archivo, igual que el resto de accesos a APIs internas de SSMS.
3. **Features/LocateObject/LocateObjectCommand.cs** — nuevo comando siguiendo el patron de `ScriptObjectCommands`: `OnBeforeQueryStatus` habilita el comando solo si hay conexion activa y `ObjectNameParser.Parse` devuelve un resultado; `Execute` arma el nombre completo, llama a `SsmsHost` para localizarlo y seleccionarlo, y muestra `MessageBox` de advertencia si no se pudo.
4. **SsmsQuickToolsPackage.cs** — registrar `new LocateObjectCommand(this, commandService).Register();` junto a los demas comandos en `InitializeAsync`.
5. **Verificacion manual** contra una instancia real de SSMS 22.6 (no hay logica pura nueva que amerite test unitario): probar tabla, vista, procedimiento, funcion y trigger, con Object Explorer ya expandido y sin expandir, con objeto inexistente, y con nombre calificado a una base de datos sin nodo abierto.

## Criterios de aceptacion

- [x] El comando "Locate in Object Explorer" aparece en Quick Tools > Query, con atajo `Ctrl+K, Ctrl+6`.
- [x] Esta deshabilitado/oculto si no hay ventana de query conectada o no hay un nombre de objeto valido seleccionado/bajo el cursor (mismo criterio que Script CREATE/ALTER).
- [x] Con el cursor sobre el nombre de una tabla existente y Object Explorer ya expandido en esa carpeta, el nodo de la tabla queda seleccionado y visible.
- [x] Igual resultado para vista, procedimiento, funcion y trigger.
- [x] Con el cursor sobre el nombre de una tabla existente pero con la carpeta `Tables` de esa base sin expandir previamente, el comando fuerza la expansion y de todas formas selecciona el nodo.
- [x] Con un nombre de objeto que no existe en la base conectada, se muestra un `MessageBox` de advertencia y no se modifica el arbol.
- [x] Con un nombre calificado a otra base de datos sin nodo abierto en Object Explorer, se muestra el mismo `MessageBox` de advertencia (no se intenta conectar ni buscar en otros servidores).
- [x] `dotnet test SsmsQuickTools.Tests` sigue pasando sin cambios (no se agregan tests nuevos; nada de esta feature es logica pura sin dependencia de SSMS).

## Decisiones tomadas y descartadas

- **Reusar `GetCandidateText` + `ObjectNameParser`** en vez de un parser nuevo: mantiene consistencia con Script CREATE/ALTER y evita duplicar logica de deteccion de objeto bajo el cursor.
- **Mismo set de tipos de objeto que Script CREATE/ALTER** (tabla, vista, procedimiento, funcion, trigger), no un alcance mas amplio (schemas, sinonimos): reduce superficie de mapeo arbol-de-Object-Explorer para la primera version.
- **No autoconectar Object Explorer** a una base/servidor sin nodo abierto: evita usar APIs internas de conexion de Object Explorer (mas riesgo de romperse) para un caso de uso secundario; el usuario conecta manualmente y reintenta.
- **No buscar en otras conexiones/servidores**: mismo motivo — limita el comando a "la conexion que ya estoy usando en esta query", que es el caso de uso principal.
- **Forzar expansion de carpetas no cargadas**: es el proposito central de la feature (si el usuario ya tuviera todo expandido y ubicado, no necesitaria el comando), asi que se acepta el costo/riesgo de forzar la carga lazy del arbol.
- **Mismo mensaje de error para "no encontrado" y "conexion/base sin nodo en el arbol"**: simplifica el manejo de errores en la primera version; no se distingue el motivo exacto al usuario.
- **Ubicacion en Quick Tools > Query, no en el menu contextual del editor**: igual que el resto del plugin, por la limitacion de merge de menus contextuales de SSMS documentada en `docs/PLAN.md` Milestone 5 y confirmada empiricamente para Script CREATE/ALTER, XML Spreadsheet y Autoreplacement.

## Riesgos identificados

- **API de Object Explorer no documentada.** No existe en el codebase ningun acceso previo al arbol de Object Explorer (`SsmsHost.cs` solo cubre `SQLEditors.dll` y `SqlWorkbench.Interfaces.dll` para conexiones/scripting). Hay que investigar e integrar contra internals nuevos, con el mismo riesgo de romperse en un update de SSMS que el resto de `SsmsHost`.
- **Carga lazy/virtualizada del arbol.** Forzar la expansion de nodos puede ser asincrona; si el codigo no espera correctamente a que los hijos se materialicen antes de buscar, el objeto puede no encontrarse aunque exista (falso "no encontrado").
- **Resolucion de schema por defecto.** Si el nombre parseado no trae schema explicito (ej. solo `Tabla` en vez de `dbo.Tabla`), hay que resolver el schema por defecto de la conexion/usuario antes de buscar el nodo — mismo tipo de ambiguedad que ya maneja `ObjectScripter` para scripting, pero aplicada a la busqueda en el arbol.
- **Ubicacion de triggers.** Los triggers no cuelgan de una carpeta plana de base de datos sino de la tabla/vista padre (`Tables > <tabla> > Triggers > <trigger>`), lo que exige primero localizar el objeto padre antes de poder localizar el trigger.
