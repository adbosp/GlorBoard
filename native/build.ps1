param(
    [string] $Zig = 'zig',
    [string] $Output = (Join-Path $PSScriptRoot '..\dist')
)

$ErrorActionPreference = 'Stop'
$compiler = (Get-Command $Zig -CommandType Application).Source
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$outputFolder = [IO.Path]::GetFullPath($Output)
$stage = Join-Path ([IO.Path]::GetTempPath()) ('GlorBoard-build-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
$res = Join-Path $stage 'GlorBoard.res'
$program = Join-Path $stage 'GlorBoard.exe'

Push-Location -LiteralPath $PSScriptRoot
try {
    & $compiler rc /fo $res GlorBoard.rc
    if ($LASTEXITCODE -ne 0) { throw 'Resource compilation failed.' }
    $arguments = @(
        'build-exe', '-target', 'x86_64-windows-gnu', '--subsystem', 'windows', '-municode', '-O', 'ReleaseSmall',
        '-fstrip', '-lc++', '-lc', '-luser32', '-lshell32', '-lnormaliz', '-cflags', '-std=c++20', '--',
        'Tray.cpp', 'Preferences.cpp', 'Keyboard.cpp', 'Composer.cpp', $res, "-femit-bin=$program"
    )
    & $compiler @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Native tray compilation failed.' }
} finally {
    Pop-Location
}

$settings = Join-Path $stage 'settings'
$publishArguments = @(
    'publish', (Join-Path $root 'src\GlorBoard\GlorBoard.csproj'), '-c', 'Release', '-r', 'win-x64',
    '--self-contained', 'true', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=None', '-p:DebugSymbols=false', '-o', $settings
)
dotnet @publishArguments
if ($LASTEXITCODE -ne 0) { throw 'Settings panel publish failed.' }

New-Item -ItemType Directory -Path $outputFolder -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $settings 'GlorBoard.exe') -Destination (Join-Path $outputFolder 'GlorBoard.Panel.exe') -Force
Copy-Item -LiteralPath $program -Destination (Join-Path $outputFolder 'GlorBoard.exe') -Force
