# SsmsQuickTools

Extensión VSIX para SQL Server Management Studio **22.6.0 en adelante**.

## Funcionalidades

1. **Quick Connect**: combo en toolbar para reconectar la ventana de query activa a un servidor/base definidos en un archivo de configuración local.
2. **Grid → Script**: copia el resultado de una consulta al portapapeles como un script `SELECT` autocontenido (CTE + `VALUES`), listo para pegar y ejecutar.
3. **Generar CREATE / Generar ALTER**: menú contextual en el editor sobre el nombre de un objeto seleccionado.

Ver el plan de diseño completo en `docs/PLAN.md`.

## Requisitos de desarrollo

- Visual Studio 2022 (17.14+) con el workload **Visual Studio extension development**.
- SSMS 22.6.0 o superior instalado localmente (para referenciar sus ensamblados y para depurar).
- .NET Framework 4.8 Developer Pack.

## Estructura

```
SsmsQuickTools.sln
SsmsQuickTools/            Proyecto VSIX principal
SsmsQuickTools.Tests/      Tests unitarios (xUnit) de lógica pura
lib/ssms22.6/              Ensamblados de SSMS 22.6.0 usados como referencia (no redistribuibles, ver lib/ssms22.6/README.md)
```

## Compilar

Abrir `SsmsQuickTools.sln` en Visual Studio 2022 y compilar. El VSIX resultante queda en
`SsmsQuickTools\bin\Debug\SsmsQuickTools.vsix`.

## Depurar

Propiedades del proyecto → Debug → Start external program → `Ssms.exe` (ruta de tu instalación).
Ver detalle en `docs/PLAN.md`.

## Instalar

Doble clic en el `.vsix` generado, o Extensions → Manage Extensions en SSMS.

## Configurar Quick Connect

Editar `%APPDATA%\SsmsQuickTools\connections.json` (se crea con un ejemplo la primera vez que se usa el combo).
Solo autenticación de Windows; el archivo no admite usuario/password.
