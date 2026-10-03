[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $OutputDirectory,
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) {
    throw 'Choose a new output directory; a baseline run never overwrites an existing fixture.'
}
New-Item -ItemType Directory -Path $output | Out-Null
$originalProfile = $env:COMPOSITOR_PROFILE
$env:COMPOSITOR_PROFILE = Join-Path $output 'profile'
$app = Join-Path $repository 'windows/dist-app'

function Invoke-Dotnet {
    param([string[]] $Arguments, [string] $Log)
    & dotnet @Arguments 2>&1 | Tee-Object -FilePath (Join-Path $output $Log)
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed ($LASTEXITCODE); see $Log." }
}

function Invoke-WindowCheck {
    param([string] $Switch, [string] $Name)
    $stdout = Join-Path $output "$Name.log"
    $stderr = Join-Path $output "$Name.stderr.log"
    $picture = Join-Path $output "$Name.png"
    $process = Start-Process -FilePath (Join-Path $app 'Compositor.Desktop.exe') `
        -ArgumentList @($Switch, ('"{0}"' -f $picture)) -WindowStyle Hidden -Wait -PassThru `
        -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    Get-Content -LiteralPath $stdout
    if ($process.ExitCode -ne 0) { throw "$Switch failed ($($process.ExitCode)); see $Name.log and $Name.stderr.log." }
    if (!(Test-Path -LiteralPath $picture)) { throw "$Switch returned without drawing its frame." }
}

function Invoke-NativeStartupCheck {
    $process = Start-Process -FilePath (Join-Path $app 'Compositor.Desktop.exe') -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $output 'native-startup.log') `
        -RedirectStandardError (Join-Path $output 'native-startup.stderr.log')
    try {
        if (!$process.WaitForInputIdle(15000) -or $process.HasExited) {
            throw 'The native Windows app did not reach input idle.'
        }
        Write-Output 'PASS native startup: published app reached input idle; no manual OS input was tested.'
    }
    finally {
        # The empty probe window holds no user document. Stop only the process this check started.
        if (!$process.HasExited) {
            $closed = $process.CloseMainWindow()
            if (!$closed -or !$process.WaitForExit(3000)) { Stop-Process -Id $process.Id }
        }
    }
}

Push-Location -LiteralPath $repository
try {
    Invoke-Dotnet -Arguments @('--version') -Log 'sdk.log'
    Invoke-Dotnet -Arguments @('build', 'windows/Compositor.slnx', '-c', $Configuration, '-warnaserror', '--nologo') -Log 'build.log'
    Invoke-Dotnet -Arguments @('test', 'windows/tests/Compositor.Core.Tests/Compositor.Core.Tests.csproj',
        '-c', $Configuration, '--no-build', '--nologo', '--logger', 'trx;LogFileName=core.trx',
        '--results-directory', (Join-Path $output 'tests')) -Log 'tests.log'
    # Bundling the runtime makes this folder runnable even on a machine without .NET 10 installed.
    Invoke-Dotnet -Arguments @('publish', 'windows/src/Compositor.Desktop/Compositor.Desktop.csproj',
        '-c', $Configuration, '-r', 'win-x64', '--self-contained', 'true', '-o', $app, '--nologo') -Log 'publish.log'
    Copy-Item -LiteralPath (Join-Path $repository 'LICENSE') -Destination (Join-Path $app 'LICENSE')
    Invoke-WindowCheck -Switch '--baseline' -Name 'baseline'
    Invoke-WindowCheck -Switch '--clicks' -Name 'clicks'
    Invoke-WindowCheck -Switch '--window' -Name 'window'
    Invoke-NativeStartupCheck
    if (!(Test-Path -LiteralPath (Join-Path $env:COMPOSITOR_PROFILE 'recent.json'))) {
        throw 'The window did not write its recent projects into the isolated profile.'
    }
    Write-Output "PASS: build, core tests and published app checks. Local unsigned app: $app"
    Write-Output "Evidence and synthetic project: $output"
}
finally {
    Pop-Location
    $env:COMPOSITOR_PROFILE = $originalProfile
}
