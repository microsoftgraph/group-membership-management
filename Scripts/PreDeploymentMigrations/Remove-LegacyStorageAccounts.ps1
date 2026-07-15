$ErrorActionPreference = "Stop"

$ScriptsDirectory = Split-Path $PSScriptRoot -Parent
. ($ScriptsDirectory + '/ReusableModules/Invoke-WithRetry.ps1')
. ($ScriptsDirectory + '/PreDeploymentMigrations/Remove-MultiLaneResources.ps1')

<#
.SYNOPSIS
Removes orphaned legacy storage accounts that were created by previous ARM deployments using uniqueString-based naming.

.DESCRIPTION
This script reconstructs the names of legacy storage accounts by deriving ARM's uniqueString
hash from the still-deployed shared functions ("fn") storage account, then deletes any matching
storage accounts that still exist. This covers the per-function pattern (e.g., "aagmmintprod<hash>"),
per-instance variants (e.g., "gugmmintprodlarge<hash>", "msgmmintprods1<hash>"), and the single
global legacy pattern (e.g., "gmmint<hash>").

The script is safe to run repeatedly - it silently skips accounts that have already been removed.

.PARAMETER SolutionAbbreviation
Abbreviation used to denote the overall solution (e.g., "gmm").

.PARAMETER EnvironmentAbbreviation
Abbreviation for the environment (e.g., "int", "ua", "prodv2").

.PARAMETER AdditionalAbbreviations
Optional array of extra function abbreviations beyond the 18 public ones.
Used by the private pipeline to pass private function abbreviations.

.PARAMETER WhatIf
SIMULATION MODE: Shows which storage accounts would be removed without making any actual changes.

.EXAMPLE
Remove-LegacyStorageAccounts -SolutionAbbreviation "gmm" -EnvironmentAbbreviation "int" -WhatIf

.EXAMPLE
Remove-LegacyStorageAccounts -SolutionAbbreviation "gmm" -EnvironmentAbbreviation "int"

.EXAMPLE
Remove-LegacyStorageAccounts -SolutionAbbreviation "gmm" -EnvironmentAbbreviation "int" -AdditionalAbbreviations @('ps','gs')

.NOTES
Per-function storage account pattern: "<abbreviation><solution><env>prod<hash>" truncated to 23 chars.
Per-instance pattern: "<abbreviation><solution><env>prod<instanceId><hash>" truncated to 23 chars.
Legacy global pattern: "<solution><env><hash>" (no truncation).
The hash is ARM's uniqueString(dataResourceGroupId), a deterministic 13-character base32 value.
ARM's uniqueString cannot be reproduced reliably outside ARM, so the hash is derived from the
still-deployed shared functions ("fn") storage account, whose name is take('fn<sol><env><hash>', 24).
#>

function Get-DeployedUniqueString {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,

        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,

        [Parameter(Mandatory = $false)]
        [string]$ResourceGroupName,

        [Parameter(Mandatory = $false)]
        [object[]]$StorageAccounts
    )

    # Every storage account in the data resource group embeds the SAME
    # uniqueString(resourceGroup().id) value. Derive it from the still-deployed
    # shared functions ("fn") storage account, named take('fn<sol><env><hash>', 24).
    # The prefix length (2 + solution + environment) is at most 11 characters, so
    # the 13-character hash is never truncated and can be read back in full.
    $prefix = "fn$SolutionAbbreviation$EnvironmentAbbreviation"

    # Reuse a caller-supplied account list to avoid a redundant ARM list call.
    if (-not $StorageAccounts) {
        $StorageAccounts = Get-AzStorageAccount -ResourceGroupName $ResourceGroupName -ErrorAction SilentlyContinue
    }

    $fnAccount = $StorageAccounts |
        Where-Object { $_.StorageAccountName -like "$prefix*" } |
        Select-Object -First 1

    if (-not $fnAccount) {
        return $null
    }

    return $fnAccount.StorageAccountName.Substring($prefix.Length)
}

function Remove-LegacyStorageAccounts {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,

        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,

        [Parameter(Mandatory = $false)]
        [string[]]$AdditionalAbbreviations = @(),

        [Parameter(Mandatory = $false)]
        [switch]$WhatIf
    )

    $context = Get-AzContext -WarningAction SilentlyContinue
    if (-not $context) {
        Write-Host "No Azure context found" -ForegroundColor Red
        Write-Host "   Please run Connect-AzAccount first to authenticate with Azure." -ForegroundColor Yellow
        return
    }

    $startTime = Get-Date
    $rgName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"

    Write-Host "Starting Legacy Storage Account Removal" -ForegroundColor Cyan
    Write-Host "   Solution: $SolutionAbbreviation" -ForegroundColor Gray
    Write-Host "   Environment: $EnvironmentAbbreviation" -ForegroundColor Gray
    Write-Host "   Mode: $(if ($WhatIf) { 'Simulation (WhatIf)' } else { 'Execution' })" -ForegroundColor Gray
    Write-Host ""

    # Get resource group ID
    $resourceGroup = Get-AzResourceGroup -Name $rgName -ErrorAction SilentlyContinue
    if (-not $resourceGroup) {
        Write-Host "Resource group '$rgName' not found" -ForegroundColor Red
        Write-Host "   Nothing to remove." -ForegroundColor Yellow
        return
    }

    # Fetch the account list once and reuse it for hash derivation and existence
    # filtering, avoiding redundant ARM calls on repeat/idempotent runs.
    $existingAccounts = Get-AzStorageAccount -ResourceGroupName $rgName -ErrorAction SilentlyContinue
    $existingNames = @($existingAccounts | ForEach-Object { $_.StorageAccountName })

    $hash = Get-DeployedUniqueString -SolutionAbbreviation $SolutionAbbreviation -EnvironmentAbbreviation $EnvironmentAbbreviation -ResourceGroupName $rgName -StorageAccounts $existingAccounts
    if (-not $hash) {
        Write-Host "Could not derive the uniqueString hash from a deployed 'fn' storage account in '$rgName'" -ForegroundColor Red
        Write-Host "   Without the hash, target account names cannot be reconstructed. Nothing will be removed." -ForegroundColor Yellow
        return
    }

    Write-Host "Derived uniqueString hash: $hash" -ForegroundColor Gray
    Write-Host ""

    # Public function abbreviations (19)
    $publicAbbreviations = @(
        'aa', 'am', 'aur', 'dau', 'gu', 'gmo', 'goo', 'js',
        'jt', 'ma', 'ms', 'nps', 'n', 'pmo', 'sdc', 'smo', 'sju', 'tcmo', 'tcu'
    )

    # Merge with additional abbreviations
    $allAbbreviations = $publicAbbreviations + $AdditionalAbbreviations

    # Build per-function storage account names
    $targetNames = @()
    foreach ($abbrev in $allAbbreviations) {
        $name = "$abbrev$SolutionAbbreviation${EnvironmentAbbreviation}prod$hash"
        $name = $name.Substring(0, [Math]::Min(23, $name.Length))
        $targetNames += $name
    }

    # Some hosts deploy multiple instances whose storage accounts embed an instance
    # identifier: "<abbrev><solution><env>prod<instanceId><hash>" (23 chars). The empty
    # identifier (base account) is already covered by the per-function loop above.
    $instanceHosts = @{
        'gu' = @('small', 'large')  # GraphUpdater
        'ms' = @('s1', 'l1')        # MessageSplitter
    }
    foreach ($abbrev in $instanceHosts.Keys) {
        foreach ($instanceId in $instanceHosts[$abbrev]) {
            $name = "$abbrev$SolutionAbbreviation${EnvironmentAbbreviation}prod$instanceId$hash"
            $name = $name.Substring(0, [Math]::Min(23, $name.Length))
            $targetNames += $name
        }
    }

    # Build legacy global storage account name
    $legacyName = "$SolutionAbbreviation$EnvironmentAbbreviation$hash"
    $targetNames += $legacyName

    # Filter to accounts that actually exist (reuses the single list fetched
    # above), so idempotent re-runs short-circuit instead of probing every
    # candidate name individually.
    $accountsToRemove = @($targetNames | Where-Object { $existingNames -contains $_ })

    Write-Host "Candidate names: $($targetNames.Count); present in '$rgName': $($accountsToRemove.Count)" -ForegroundColor Cyan

    if ($accountsToRemove.Count -eq 0) {
        Write-Host "No legacy storage accounts found - nothing to remove." -ForegroundColor Green
        return
    }

    foreach ($name in $accountsToRemove) {
        Write-Host "   - $name" -ForegroundColor Gray
    }
    Write-Host ""

    if ($WhatIf) {
        Write-Host "SIMULATION MODE (WhatIf) - No changes will be made" -ForegroundColor Magenta
        Write-Host "-----------------------------------------------------------" -ForegroundColor Magenta
        foreach ($name in $accountsToRemove) {
            Remove-StorageAccount -StorageAccountName $name -ResourceGroupName $rgName -WhatIf
        }
        Write-Host ""
        Write-Host "To execute these changes, run the script without the -WhatIf parameter" -ForegroundColor Cyan
        Write-Host "-----------------------------------------------------------" -ForegroundColor Magenta
        return
    }

    Write-Host ""
    Write-Host "Executing removals..." -ForegroundColor Cyan
    foreach ($name in $accountsToRemove) {
        Remove-StorageAccount -StorageAccountName $name -ResourceGroupName $rgName
    }

    # Final summary
    $duration = (Get-Date) - $startTime
    Write-Host ""
    Write-Host "Legacy storage account removal completed!" -ForegroundColor Green
    Write-Host "   Duration: $($duration.ToString('mm\:ss'))" -ForegroundColor Gray
    Write-Host "   Accounts removed: $($accountsToRemove.Count)" -ForegroundColor Gray
}
