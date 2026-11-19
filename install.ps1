<#
.SYNOPSIS
Builds dotpeek-mcp and registers it in Claude Code.

.DESCRIPTION
One command, from any PowerShell window:

    irm https://raw.githubusercontent.com/matiasdante/dotpeek-mcp/v0.1.0/install.ps1 | iex

Downloads the tagged source to %LOCALAPPDATA%\dotpeek-mcp\<version>, builds it against the local
dotPeek install, and runs `claude mcp add` (user scope). Without Claude Code, it prints the JSON
snippet for any other MCP client. Run from a clone (.\install.ps1), it builds the working copy instead.

Overrides, through environment variables:
    DOTPEEK_HOME          dotPeek install folder (default: %LOCALAPPDATA%\JetBrains\Installations\dotPeek262)
    DOTPEEK_MCP_VERSION   tag to install (default: v0.1.0)
#>

# Everything lives in a function and errors are thrown, never `exit`: under `irm | iex` an exit would close the user's window.
function Install-DotPeekMcp {
    [CmdletBinding()]
    param()

    $ErrorActionPreference = 'Stop'
    $repo = 'matiasdante/dotpeek-mcp'
    $version = if ($env:DOTPEEK_MCP_VERSION) { $env:DOTPEEK_MCP_VERSION } else { 'v0.1.0' }
    $customHome = [bool]$env:DOTPEEK_HOME
    $dotPeekHome = if ($customHome) { $env:DOTPEEK_HOME.TrimEnd('\') } else { Join-Path $env:LOCALAPPDATA 'JetBrains\Installations\dotPeek262' }

    if ($env:OS -ne 'Windows_NT') { throw 'dotpeek-mcp runs on Windows x64 only.' }
    # The version ends up in a URL and a folder name.
    if ($version -notmatch '^v\d+\.\d+\.\d+$') { throw "DOTPEEK_MCP_VERSION must look like v1.2.3, got '$version'." }
    if (-not (Test-Path (Join-Path $dotPeekHome 'JetBrains.Decompiler.Core.dll'))) {
        throw "dotPeek 2026.2 not found in '$dotPeekHome'. Install it, or set DOTPEEK_HOME to its folder."
    }
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'The .NET SDK is required to build. Install it from https://dot.net and run this again.'
    }

    if ($PSScriptRoot -and (Test-Path (Join-Path $PSScriptRoot 'DotPeekMcp.csproj'))) {
        $source = $PSScriptRoot
        Write-Output "Building the working copy in $source"
    }
    else {
        $root = Join-Path $env:LOCALAPPDATA 'dotpeek-mcp'
        $source = Join-Path $root $version
        if (-not (Test-Path (Join-Path $source 'DotPeekMcp.csproj'))) {
            Write-Output "Downloading dotpeek-mcp $version"
            # Windows PowerShell 5.1 may default to TLS 1.0, which GitHub rejects.
            [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
            New-Item -ItemType Directory -Force -Path $root | Out-Null
            $zip = Join-Path $root "$version.zip"
            $staging = Join-Path $root "staging-$([guid]::NewGuid())"
            try {
                Invoke-WebRequest "https://github.com/$repo/archive/refs/tags/$version.zip" -OutFile $zip -UseBasicParsing
                Expand-Archive -Path $zip -DestinationPath $staging
                # GitHub wraps the archive in a single "dotpeek-mcp-<version>" folder.
                Move-Item -Path (Get-ChildItem $staging -Directory | Select-Object -First 1).FullName -Destination $source
            }
            finally {
                Remove-Item $zip, $staging -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Write-Output 'Building (Release)'
    & dotnet build (Join-Path $source 'DotPeekMcp.csproj') -c Release -v q -nologo "-p:DotPeekHome=$dotPeekHome"
    if ($LASTEXITCODE -ne 0) { throw "Build failed (exit code $LASTEXITCODE)." }
    $exe = Join-Path $source 'bin\Release\net472\DotPeekMcp.exe'

    if (Get-Command claude -ErrorAction SilentlyContinue) {
        # Windows PowerShell 5.1 turns redirected native stderr into errors; with 'Stop' that would abort here.
        $ErrorActionPreference = 'Continue'
        & claude mcp get dotpeek *> $null
        $registered = $LASTEXITCODE -eq 0
        $ErrorActionPreference = 'Stop'
        if ($registered) {
            Write-Output "Claude Code already has a 'dotpeek' server; left it unchanged."
            Write-Output "To point it at this build: claude mcp remove dotpeek --scope user, then run this installer again."
        }
        else {
            $envArgs = if ($customHome) { @('--env', "DOTPEEK_HOME=$dotPeekHome") } else { @() }
            & claude mcp add --scope user @envArgs dotpeek -- $exe
            if ($LASTEXITCODE -ne 0) { throw "claude mcp add failed (exit code $LASTEXITCODE)." }
            Write-Output "Registered 'dotpeek' in Claude Code. Open a new session and check /mcp."
        }
    }
    else {
        $config = @{ mcpServers = @{ dotpeek = @{ command = $exe } } }
        if ($customHome) { $config.mcpServers.dotpeek.env = @{ DOTPEEK_HOME = $dotPeekHome } }
        Write-Output 'Claude Code not found. Add this to your MCP client configuration:'
        Write-Output ($config | ConvertTo-Json -Depth 5)
    }
    Write-Output "Installed: $exe"
}

Install-DotPeekMcp
