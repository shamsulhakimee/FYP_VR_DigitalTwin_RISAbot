#Requires -Version 5.1
<#
.SYNOPSIS
    Build, verify, and package RISA-Studio for distribution.
.DESCRIPTION
    Complete ship-ready pipeline:
      1. Pre-flight system checks (.NET SDK, Inno Setup, OS)
      2. Clean Release publish (self-contained, win-x64, single-file)
      3. Post-publish verification
      4. Inno Setup compilation -> RISA-Studio-v2.0.0-Setup.exe
      5. Summary report
.PARAMETER SkipBuild
    Skip the dotnet publish step (re-use existing publish output).
.PARAMETER SkipInstaller
    Skip the Inno Setup compilation step.
.EXAMPLE
    .\Build-Installer.ps1
    .\Build-Installer.ps1 -SkipBuild
#>

param(
    [switch]$SkipBuild,
    [switch]$SkipInstaller
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ---- Paths ------------------------------------------------------------------
$Root        = Split-Path -Parent $PSScriptRoot   # repo root (parent of installer\)
$ProjectFile = Join-Path $Root "RisaBotApp\RisaBotApp.csproj"
$PublishDir  = Join-Path $PSScriptRoot "publish"
$OutputDir   = Join-Path $PSScriptRoot "output"
$IssFile     = Join-Path $PSScriptRoot "RisaBotApp.iss"

# Locate ISCC.exe (user install or system-wide)
$IsccCandidates = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)
$IsccPath = $IsccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

$AppVersion     = "2.0.0"
$MinDotNetMajor = 10

# ---- Helpers ----------------------------------------------------------------
function Write-Header([string]$msg) {
    Write-Host ""
    Write-Host ("=" * 62) -ForegroundColor Cyan
    Write-Host "  $msg" -ForegroundColor Cyan
    Write-Host ("=" * 62) -ForegroundColor Cyan
}
function Write-Pass([string]$msg) { Write-Host "  [PASS]  $msg" -ForegroundColor Green  }
function Write-Fail([string]$msg) { Write-Host "  [FAIL]  $msg" -ForegroundColor Red    }
function Write-Warn([string]$msg) { Write-Host "  [WARN]  $msg" -ForegroundColor Yellow }
function Write-Info([string]$msg) { Write-Host "  [INFO]  $msg" -ForegroundColor Gray   }

$Failures = [System.Collections.Generic.List[string]]::new()
function Fail([string]$msg) { $Failures.Add($msg); Write-Fail $msg }


# =============================================================================
#  STEP 1 -- PRE-FLIGHT CHECKS
# =============================================================================
Write-Header "STEP 1 / 5 -- Pre-flight System Checks"

# OS version check
$os    = Get-CimInstance Win32_OperatingSystem
$build = [int]($os.BuildNumber)
Write-Info "OS: $($os.Caption) (Build $build)"
if ($build -ge 17763) {
    Write-Pass "Windows 10 1809+ -- compatible"
} else {
    Fail "Windows build $build is too old. Require build 17763 (Windows 10 1809)+."
}

# Architecture
$arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
Write-Info "Architecture: $arch"
if ($arch -eq "X64") {
    Write-Pass "x64 architecture -- compatible"
} else {
    Write-Warn "Architecture is $arch -- this installer targets win-x64 only"
}

# .NET SDK
try {
    $sdkVersion = (& dotnet --version 2>&1).ToString().Trim()
    $sdkMajor   = [int]($sdkVersion.Split('.')[0])
    Write-Info ".NET SDK: $sdkVersion"
    if ($sdkMajor -ge $MinDotNetMajor) {
        Write-Pass ".NET SDK $sdkVersion -- OK"
    } else {
        Fail ".NET SDK $sdkVersion found but $MinDotNetMajor.x required. Install from https://dot.net"
    }
} catch {
    Fail ".NET SDK not found. Install from https://dotnet.microsoft.com/download"
}

# Inno Setup
if ($IsccPath) {
    Write-Pass "Inno Setup 6 found: $IsccPath"
} else {
    Fail "Inno Setup 6 (ISCC.exe) not found. Run: winget install JRSoftware.InnoSetup"
}

# Project file
if (Test-Path $ProjectFile) {
    Write-Pass "Project file: $ProjectFile"
} else {
    Fail "Project file missing: $ProjectFile"
}

# ISS script
if (Test-Path $IssFile) {
    Write-Pass "Installer script: $IssFile"
} else {
    Fail "Installer script missing: $IssFile"
}

# App icon
$IconPath = Join-Path $Root "RisaBotApp\risa.ico"
if (Test-Path $IconPath) {
    Write-Pass "App icon found: risa.ico"
} else {
    Write-Warn "App icon not found at $IconPath -- installer will use default Inno Setup icon"
}

# Stop if critical checks failed
if ($Failures.Count -gt 0) {
    Write-Host ""
    Write-Host "PRE-FLIGHT FAILED -- resolve the above issues before building." -ForegroundColor Red
    exit 1
}


# =============================================================================
#  STEP 2 -- PUBLISH
# =============================================================================
Write-Header "STEP 2 / 5 -- dotnet publish (self-contained win-x64)"

if ($SkipBuild) {
    Write-Info "Skipping build (-SkipBuild specified). Using existing publish output."
} else {
    if (Test-Path $PublishDir) {
        Write-Info "Removing previous publish output..."
        Remove-Item $PublishDir -Recurse -Force
    }

    Write-Info "Running: dotnet publish ..."
    $publishArgs = @(
        "publish", $ProjectFile,
        "--configuration", "Release",
        "--runtime",       "win-x64",
        "--self-contained","true",
        "-p:PublishSingleFile=true",
        "-p:PublishReadyToRun=true",
        "--output",        $PublishDir
    )
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Host "dotnet publish failed (exit $LASTEXITCODE)" -ForegroundColor Red
        exit 1
    }
    Write-Pass "dotnet publish succeeded"
}


# =============================================================================
#  STEP 3 -- POST-PUBLISH VERIFICATION
# =============================================================================
Write-Header "STEP 3 / 5 -- Post-publish Verification"

$ExePath = Join-Path $PublishDir "RisaBotApp.exe"
$PdbPath = Join-Path $PublishDir "RisaBotApp.pdb"

# EXE must exist
if (Test-Path $ExePath) {
    $sizeMB = [math]::Round((Get-Item $ExePath).Length / 1MB, 1)
    Write-Pass "RisaBotApp.exe present -- $sizeMB MB"
    if ($sizeMB -lt 10) {
        Write-Warn "EXE appears small ($sizeMB MB). Expected 140+ MB for self-contained build."
    }
} else {
    Fail "RisaBotApp.exe not found in publish output at: $ExePath"
}

# PDB check
if (Test-Path $PdbPath) {
    Write-Warn "RisaBotApp.pdb present in publish output -- debug symbols will be included in installer"
} else {
    Write-Pass "No PDB in publish output -- clean release"
}

# Required WPF native DLLs
$RequiredDlls = @(
    "D3DCompiler_47_cor3.dll",
    "PenImc_cor3.dll",
    "PresentationNative_cor3.dll",
    "vcruntime140_cor3.dll",
    "wpfgfx_cor3.dll"
)
foreach ($dll in $RequiredDlls) {
    if (Test-Path (Join-Path $PublishDir $dll)) {
        Write-Pass "Found WPF dependency: $dll"
    } else {
        Fail "Missing required WPF DLL: $dll"
    }
}

# File version metadata
if (Test-Path $ExePath) {
    try {
        $vi = (Get-Item $ExePath).VersionInfo
        Write-Info "Product   : $($vi.ProductName) $($vi.ProductVersion)"
        Write-Info "File Ver  : $($vi.FileVersion)"
        Write-Info "Company   : $($vi.CompanyName)"
        if ($vi.FileVersion -like "$AppVersion*") {
            Write-Pass "Version $AppVersion confirmed in binary metadata"
        } else {
            Write-Warn "Binary version ($($vi.FileVersion)) differs from expected $AppVersion"
        }
    } catch {
        Write-Warn "Could not read file version info from EXE: $_"
    }
}

if ($Failures.Count -gt 0) {
    Write-Host ""
    Write-Fail "Verification failed -- fix the above issues before packaging."
    exit 1
}


# =============================================================================
#  STEP 4 -- BUILD INSTALLER
# =============================================================================
Write-Header "STEP 4 / 5 -- Compile Inno Setup Installer"

if ($SkipInstaller) {
    Write-Info "Skipping installer (-SkipInstaller specified)."
} else {
    if (-not (Test-Path $OutputDir)) {
        New-Item -ItemType Directory -Path $OutputDir | Out-Null
    }

    Write-Info "Running ISCC.exe on: $IssFile"
    Push-Location $PSScriptRoot
    try {
        & $IsccPath $IssFile
        if ($LASTEXITCODE -ne 0) {
            Fail "ISCC.exe failed (exit $LASTEXITCODE)"
        } else {
            Write-Pass "Inno Setup compilation succeeded"
        }
    } finally {
        Pop-Location
    }

    $SetupExe = Join-Path $OutputDir "RISA-Studio-v$AppVersion-Setup.exe"
    if (Test-Path $SetupExe) {
        $setupMB = [math]::Round((Get-Item $SetupExe).Length / 1MB, 1)
        Write-Pass "Installer output: $SetupExe -- $setupMB MB"
    } else {
        Fail "Installer EXE not found at expected path: $SetupExe"
    }
}


# =============================================================================
#  STEP 5 -- FINAL SUMMARY
# =============================================================================
Write-Header "STEP 5 / 5 -- Summary"

if ($Failures.Count -eq 0) {
    Write-Host ""
    Write-Host "  ################################################" -ForegroundColor Green
    Write-Host "  ##    BUILD SUCCESSFUL -- SHIP READY          ##" -ForegroundColor Green
    Write-Host "  ################################################" -ForegroundColor Green
    Write-Host ""

    $SetupExe = Join-Path $OutputDir "RISA-Studio-v$AppVersion-Setup.exe"
    if (Test-Path $SetupExe) {
        $setupMB = [math]::Round((Get-Item $SetupExe).Length / 1MB, 1)
        $exeMB   = [math]::Round((Get-Item $ExePath).Length   / 1MB, 1)
        Write-Host "  Installer : $SetupExe" -ForegroundColor White
        Write-Host "  Size      : $setupMB MB  (payload EXE: $exeMB MB)" -ForegroundColor White
        Write-Host ""
        Write-Host "  No runtime installation needed on end-user machine." -ForegroundColor White
        Write-Host "  Supported : Windows 10 (1809+) / Windows 11 -- x64" -ForegroundColor White
        Write-Host ""
    }
} else {
    Write-Host ""
    Write-Host "  BUILD FAILED -- $($Failures.Count) issue(s) found:" -ForegroundColor Red
    foreach ($f in $Failures) {
        Write-Host "    * $f" -ForegroundColor Red
    }
    Write-Host ""
    exit 1
}
