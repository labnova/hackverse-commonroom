#Requires -Version 7.0
<#
.SYNOPSIS
    Removes what deploy/setup.ps1 created, as recorded in deploy/.state.json.
.DESCRIPTION
    Asks to type the PREFIX before deleting anything. -DryRun prints the commands without running them.
#>
[CmdletBinding()]
param([switch]$DryRun)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$DeployDir  = $PSScriptRoot
$Repo       = (Resolve-Path (Join-Path $DeployDir '..')).Path
$StateFile  = Join-Path $DeployDir '.state.json'
$Infra      = Join-Path $Repo 'infra/Commonroom.Infra'
$CdkContext = Join-Path $Infra 'cdk.context.json'
$Stack      = 'CommonroomAuthStack'
$Cdk        = @('--yes', 'aws-cdk@2')

function Write-Info([string]$Text) { Write-Host "   $Text" }
function Read-JsonFile([string]$Path) {
    if (Test-Path $Path) { $raw = Get-Content $Path -Raw; if ($raw -and $raw.Trim()) { return ($raw | ConvertFrom-Json -AsHashtable) } }
    return @{}
}
function Get-Value([hashtable]$Data, [string]$Key) { if ($Data.ContainsKey($Key)) { return [string]$Data[$Key] } else { return '' } }
function Invoke-Native([string]$Exe, [string[]]$Arguments, [string]$WorkDir) {
    $shown = ($Arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
    Write-Host "   + $Exe $shown"
    if ($DryRun) { return }
    if ($WorkDir) { Push-Location $WorkDir }
    try {
        & $Exe @Arguments
        if ($LASTEXITCODE -ne 0) { throw "ERROR: '$Exe $($Arguments[0])' failed with exit code $LASTEXITCODE." }
    } finally { if ($WorkDir) { Pop-Location } }
}
function Remove-LocalFile([string]$Path) {
    Write-Host "   + remove $([IO.Path]::GetRelativePath($Repo, $Path))"
    if (-not $DryRun) { Remove-Item $Path -Force }
}

try {
    if (-not (Test-Path $StateFile)) { throw 'ERROR: deploy/.state.json not found: nothing to tear down.' }
    $state = Read-JsonFile $StateFile
    $prefix = Get-Value $state 'prefix'
    if (-not $prefix) { throw 'ERROR: deploy/.state.json has no prefix.' }
    $rg = Get-Value $state 'resourceGroup'; $foundry = Get-Value $state 'foundry'; $loc = Get-Value $state 'azLocation'
    $domainPrefix = Get-Value $state 'domainPrefix'
    $hasRg = (Get-Value $state 'rgDone') -eq '1'
    $hasFoundry = (Get-Value $state 'foundryDone') -eq '1'
    $hasCognito = (Get-Value $state 'cognitoDone') -eq '1'
    $env:AWS_PROFILE = Get-Value $state 'awsProfile'
    $env:AWS_REGION = Get-Value $state 'awsRegion'
    $env:AWS_DEFAULT_REGION = $env:AWS_REGION
    $env:CDK_DEFAULT_REGION = $env:AWS_REGION

    Write-Host "This deletes, for PREFIX=${prefix}:"
    if ($hasRg) { Write-Info "Azure resource group $rg (Foundry, App Service, Static Web App) + purge of $foundry" }
    if ($hasCognito) { Write-Info "AWS stack $Stack in $env:AWS_REGION (user pool, users, app clients)" }
    Write-Info "deploy/.state.json, and infra/Commonroom.Infra/cdk.context.json if it holds domainPrefix=$domainPrefix"

    if ($DryRun) {
        Write-Info 'dry-run: confirmation and login checks skipped'
    } else {
        if ((Read-Host 'Type the PREFIX to confirm') -cne $prefix) { throw 'Not confirmed: nothing deleted.' }
        if ($hasCognito) {
            & aws sts get-caller-identity *> $null
            if ($LASTEXITCODE -ne 0) { throw "ERROR: AWS session for profile '$env:AWS_PROFILE' missing or expired: run 'aws login --profile $env:AWS_PROFILE', then retry." }
        }
        if ($hasRg) {
            & az account get-access-token --query expiresOn -o tsv *> $null
            if ($LASTEXITCODE -ne 0) { throw "ERROR: Azure session missing or expired: run 'az login', then retry." }
        }
    }

    if ($hasRg) {
        Invoke-Native 'az' @('group', 'delete', '-n', $rg, '--yes')
        if ($hasFoundry) { Invoke-Native 'az' @('cognitiveservices', 'account', 'purge', '-n', $foundry, '-g', $rg, '-l', $loc) }
    }
    if ($hasCognito) { Invoke-Native 'npx' ($Cdk + @('destroy', $Stack, '--force')) $Infra }

    if ((Test-Path $CdkContext) -and (Get-Value (Read-JsonFile $CdkContext) 'domainPrefix') -eq $domainPrefix) { Remove-LocalFile $CdkContext }
    Remove-LocalFile $StateFile

    Write-Host ''
    Write-Info 'Left in place (local only): apps/demo-host/.env.local, .env.production.local and the backend user-secrets.'
    Write-Info 'To clear them: dotnet user-secrets clear --project backends/Commonroom.ReferenceApi'
    if ($DryRun) { Write-Info 'DRY RUN: nothing was executed or deleted.' }
} catch {
    Write-Host "`n$($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
