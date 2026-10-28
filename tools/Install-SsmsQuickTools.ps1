<#
.SYNOPSIS
    Instala la extension SsmsQuickTools en SQL Server Management Studio 22.6+.

.DESCRIPTION
    A diferencia de instalar el .vsix a mano, este script:

      1. Exige que NO haya ningun proceso Ssms en ejecucion. Si SSMS esta abierto,
         VSIXInstaller copia los archivos pero no puede escribir el pkgdef en
         %LOCALAPPDATA%\Microsoft\SSMS\<instancia>\privateregistry.bin porque el
         proceso lo tiene bloqueado; al cerrar SSMS, ese proceso vuelca su registro
         en memoria sobre el archivo y borra lo que el instalador habia escrito.
         Resultado: la extension figura instalada pero nunca se carga y el menu no
         aparece.
      2. Desinstala la version previa antes de instalar, para no dejar dos carpetas
         de extension conviviendo.
      3. Verifica, leyendo privateregistry.bin, que el GUID del paquete y el del
         command set hayan quedado registrados. Ese chequeo es el que distingue
         "instalado" de "instalado y funcional".

    No toca %APPDATA%\SsmsQuickTools\ (connections.json, autoreplacement.xml).

.CODES DE SALIDA
      0 = instalada y registrada
      1 = SSMS en ejecucion
      2 = no se encontro VSIXInstaller.exe
      3 = no se encontro el .vsix a instalar
      4 = no se encontro privateregistry.bin
      5 = el pkgdef no quedo registrado

.PARAMETER VsixPath
    Opcional. Ruta al .vsix. Por defecto, bin\Debug\net48\SsmsQuickTools.vsix.

.PARAMETER SsmsPath
    Opcional. Raiz de SSMS 22 (la carpeta que contiene Common7\IDE).

.EXAMPLE
    .\Install-SsmsQuickTools.ps1

.EXAMPLE
    .\Install-SsmsQuickTools.ps1 -VsixPath 'D:\build\SsmsQuickTools.vsix'
#>
param(
    [string]$VsixPath,
    [string]$SsmsPath
)

$ErrorActionPreference = 'Stop'

# Debe coincidir con Identity/@Id de SsmsQuickTools\source.extension.vsixmanifest.
$VsixId = 'SsmsQuickTools.a1c9e6a2-6b7a-4a3e-9b1d-7e6f2f4a9c10'

# GUIDs de PackageGuids (SsmsQuickTools\Guids.cs). El primero lo escribe la clave
# $RootKey$\Packages, el segundo aparece en Menus/AutoLoadPackages.
$PackageGuid = '016d6f06'
$CmdSetGuid = '2e7f3eab'

# --- 1. SSMS cerrado -------------------------------------------------------

$ssms = @(Get-Process -Name 'Ssms' -ErrorAction SilentlyContinue)
if ($ssms.Count -gt 0) {
    Write-Host "SSMS sigue en ejecucion (PID: $($ssms.Id -join ', '))." -ForegroundColor Red
    Write-Host 'Cerralo por completo (guardando lo que necesites) y volve a correr el script.' -ForegroundColor Red
    Write-Host 'Si el registro se escribe con SSMS abierto, el proceso lo sobrescribe al salir.' -ForegroundColor Yellow
    exit 1
}

# --- 2. VSIXInstaller ------------------------------------------------------

function Get-VsixInstallerPath([string]$Root) {
    if (-not $Root) { return $null }
    $candidate = Join-Path $Root 'Common7\IDE\VSIXInstaller.exe'
    if (Test-Path -LiteralPath $candidate) { return $candidate }
    return $null
}

function Find-VsixInstaller([string]$OverridePath) {
    if ($OverridePath) { return Get-VsixInstallerPath $OverridePath }

    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswhere) {
        $roots = & $vswhere -products * -prerelease -property installationPath
        foreach ($root in $roots) {
            # Solo instalaciones de SSMS (VS comun tambien trae VSIXInstaller.exe).
            if (-not (Test-Path -LiteralPath (Join-Path $root 'Common7\IDE\Ssms.exe'))) { continue }
            $found = Get-VsixInstallerPath $root
            if ($found) { return $found }
        }
    }

    return Get-VsixInstallerPath 'C:\Program Files\Microsoft SQL Server Management Studio 22\Release'
}

$VsixInstaller = Find-VsixInstaller $SsmsPath
if (-not $VsixInstaller) {
    Write-Host 'No se encontro VSIXInstaller.exe. Usa -SsmsPath con la raiz de SSMS 22.' -ForegroundColor Red
    exit 2
}

# --- 3. .vsix a instalar ---------------------------------------------------

if (-not $VsixPath) {
    $VsixPath = Join-Path $PSScriptRoot '..\SsmsQuickTools\bin\Debug\net48\SsmsQuickTools.vsix'
}
if (-not (Test-Path -LiteralPath $VsixPath)) {
    Write-Host "No se encontro el .vsix: $VsixPath" -ForegroundColor Red
    Write-Host 'Compila primero: dotnet build SsmsQuickTools\SsmsQuickTools.csproj -p:DeployExtension=false' -ForegroundColor Yellow
    exit 3
}
$VsixPath = (Resolve-Path -LiteralPath $VsixPath).Path

$manifest = Join-Path (Split-Path $VsixPath -Parent) 'extension.vsixmanifest'
$version = 'desconocida'
if (Test-Path -LiteralPath $manifest) {
    try {
        $x = [xml](Get-Content -Raw -LiteralPath $manifest)
        $version = $x.PackageManifest.Metadata.Identity.Version
    } catch { }
}

Write-Host "VSIXInstaller: $VsixInstaller"
Write-Host "VSIX:          $VsixPath (v$version)"

# --- 4. desinstalar la version previa --------------------------------------

Write-Host 'Desinstalando version previa (si existe) ...'
& $VsixInstaller '/quiet' "/uninstall:$VsixId" | Out-Null
Write-Host "  codigo de salida: $LASTEXITCODE"

# --- 5. instalar -----------------------------------------------------------

Write-Host 'Instalando ...'
& $VsixInstaller '/quiet' $VsixPath | Out-Null
$installExit = $LASTEXITCODE
Write-Host "  codigo de salida: $installExit"

# --- 6. verificar registro -------------------------------------------------

$instanceRoot = Join-Path $env:LOCALAPPDATA 'Microsoft\SSMS'
$hive = $null
if (Test-Path -LiteralPath $instanceRoot) {
    foreach ($dir in (Get-ChildItem -LiteralPath $instanceRoot -Directory -ErrorAction SilentlyContinue |
                      Where-Object { $_.Name -notlike '*Exp' })) {
        $candidate = Join-Path $dir.FullName 'privateregistry.bin'
        if (Test-Path -LiteralPath $candidate) { $hive = $candidate; break }
    }
}
if (-not $hive) {
    Write-Host 'No se encontro privateregistry.bin: no se puede verificar el registro.' -ForegroundColor Yellow
    exit 4
}

$bytes = [System.IO.File]::ReadAllBytes($hive)
$text = [System.Text.Encoding]::Unicode.GetString($bytes)

$hasPackage = $text.Contains($PackageGuid)
$hasCmdSet = $text.Contains($CmdSetGuid)

Write-Host ''
Write-Host "Verificacion del registro ($hive):"
Write-Host ("  Packages    {0} : {1}" -f $PackageGuid, $hasPackage)
Write-Host ("  Menus/AutoLoad {0} : {1}" -f $CmdSetGuid, $hasCmdSet)

if ($hasPackage -and $hasCmdSet) {
    Write-Host ''
    Write-Host 'OK: extension registrada. Abri SSMS y mira Tools > Quick Tools.' -ForegroundColor Green
    Write-Host 'Si el menu sigue sin aparecer, arranca SSMS con /log y revisa ActivityLog.xml' -ForegroundColor Gray
    exit 0
}

Write-Host ''
Write-Host 'FALLO: el pkgdef no quedo en el registro privado.' -ForegroundColor Red
Write-Host "VSIXInstaller devolvio $installExit." -ForegroundColor Yellow
Write-Host 'Revisa el log en %TEMP%\dd_VSIXInstaller_*.log y %TEMP%\dd_setup_*.log' -ForegroundColor Yellow
exit 5
