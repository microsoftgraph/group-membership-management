$ErrorActionPreference = "Stop"

<#
.SYNOPSIS
Removes RBAC role assignments for function scripts.

.DESCRIPTION
This script removes RBAC role assignments that were created outside of bicep for web apps.
The reason for this is to clean up any role assignments that may conflict with bicep-managed assignments.
It checks role assignments on both Key Vaults and Storage Accounts associated with the web apps.

.PARAMETER SolutionAbbreviation
Abbreviation used to denote the overall solution (e.g., "gmm")

.PARAMETER EnvironmentAbbreviation
Abbreviation for the environment (e.g., "dev", "prod")

.EXAMPLE
Remove-FunctionScriptRBACRoles -SolutionAbbreviation "gmm" -EnvironmentAbbreviation "dev" -SkipConfirmation

#>

function Invoke-RoleAssignmentRemoval {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory = $true)]
		[string]$DisplayName,
		[Parameter(Mandatory = $true)]
		[string]$RoleAssignmentName,
		[Parameter(Mandatory = $true)]
		$RoleAssignment,
		[Parameter(Mandatory = $true)]
		[string]$ResourceName,
		[Parameter(Mandatory = $true)]
		$ResourceId,
		[Parameter(Mandatory = $false)]
		[switch]$SkipConfirmation
	)

	Write-Host "  Removing role assignment: $RoleAssignmentName for $DisplayName on $ResourceName" -ForegroundColor Yellow

	if (-not $SkipConfirmation) {
		Write-Host ""
		Write-Host "  ⚠️  WARNING: This will PERMANENTLY DELETE the following role assignment:" -ForegroundColor Red
		Write-Host "     • Role: $($RoleAssignment.RoleDefinitionName)" -ForegroundColor Yellow
		Write-Host "     • Principal: $DisplayName" -ForegroundColor Yellow
		Write-Host "     • Resource: $ResourceName" -ForegroundColor Yellow
		Write-Host ""
		Write-Host "     This action cannot be undone!" -ForegroundColor Red
		$confirmation = Read-Host "Do you want to continue? (yes/no)"
		if ($confirmation -notmatch '^(y|yes)$') {
			Write-Host "  ❌ Removal cancelled by user" -ForegroundColor Red
			return
		}
	}
	else {
		Write-Host ""
		Write-Host "  🚀 Confirmation skipped - proceeding with removal automatically" -ForegroundColor Yellow
	}

	Remove-AzRoleAssignment -ObjectId $RoleAssignment.ObjectId -RoleDefinitionId $RoleAssignment.RoleDefinitionId -Scope $ResourceId
	Write-Host "    ⚠️ Role assignment removed, will be recreated in bicep." -ForegroundColor Green
}

function Process-RoleAssignments {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory = $true)]
		[string]$ResourceName,

		[Parameter(Mandatory = $true)]
		$ResourceId,

		[Parameter(Mandatory = $true)]
		[object[]]$RoleAssignments,

		[Parameter(Mandatory = $false)]
		[switch]$SkipConfirmation
	)

	foreach ($roleAssignment in $RoleAssignments) {
		Write-Host "Processing role assignment for: $($roleAssignment.DisplayName), Role: $($roleAssignment.RoleDefinitionName)" -ForegroundColor Cyan

		$expectedGuid = Generate-BicepRoleAssignmentGuid -RoleDefinitionId $roleAssignment.RoleDefinitionId `
			-PrincipalId $roleAssignment.ObjectId `
			-ResourceId $ResourceId

		Write-Host "  Role assignment name: $($roleAssignment.RoleAssignmentName)"
		Write-Host "  Expected role assignment name: $($expectedGuid)"

		if ($roleAssignment.RoleAssignmentName -ne $expectedGuid.ToString()) {
			Invoke-RoleAssignmentRemoval -DisplayName $roleAssignment.DisplayName `
				-RoleAssignmentName $roleAssignment.RoleAssignmentName `
				-RoleAssignment $roleAssignment `
				-ResourceName $ResourceName `
				-ResourceId $ResourceId `
				-SkipConfirmation:$SkipConfirmation
		} else {
			Write-Host "  ✅ Names match! Preserving Bicep-managed role assignment: $($roleAssignment.RoleAssignmentName)" -ForegroundColor Green
		}
	}
}

function Select-StorageAccount {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory = $true)]
		[object[]]$AllAccounts,

		[Parameter(Mandatory = $true)]
		[string]$Prefix,

		[Parameter(Mandatory = $true)]
		[AllowEmptyString()]
		[string]$SizeIdentifier
	)

	if ($AllAccounts.Count -eq 1) {
		return $AllAccounts[0]
	}

	# Multiple matches found
	if ($SizeIdentifier -eq "") {
		# No size identifier given, try to filter out any accounts with known size keywords
		$sizeKeywords = @("small", "medium", "large", "onboarding", "s1", "m2", "l1", "o1")
		$prefixes = $sizeKeywords | ForEach-Object {
			$newPrefix = "$prefix$_"
			if ($newPrefix.Length -gt 23) {
				$newPrefix = $newPrefix.Substring(0, 23)
			}
			return $newPrefix
		}

		$filteredMatches = $AllAccounts | Where-Object {
			$acctName = $_.StorageAccountName.ToLower()
			$matchesSize = $prefixes | ForEach-Object { $acctName -like "$_*" }
			-not ($matchesSize -contains $true)
		}

		if ($filteredMatches.Count -eq 1) {
			return $filteredMatches[0]
		} elseif ($filteredMatches.Count -gt 1) {
			Write-Warning "Multiple non-size storage accounts found. Using $($filteredMatches[0].StorageAccountName)."
			return $filteredMatches[0]
		} else {
			Write-Warning "No non-size storage account found. Using $($AllAccounts[0].StorageAccountName)."
			return $AllAccounts[0]
		}
	} else {
		# If we have a size identifier, just pick the first match
		Write-Warning "Multiple matches found. Using $($AllAccounts[0].StorageAccountName)."
		return $AllAccounts[0]
	}
}

function Remove-FunctionScriptRBACRoles {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory = $true)]
		[string]$SolutionAbbreviation,
		[Parameter(Mandatory = $true)]
		[string]$EnvironmentAbbreviation,
		[Parameter(Mandatory = $false)]
		[switch]$SkipConfirmation
	)

	Write-Verbose "Remove-FunctionScriptRBACRoles starting..."

	$computeResourceGroupName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation"
	$allWebapps = Get-AzWebApp -ResourceGroupName $computeResourceGroupName

	$dataResourceGroupName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
	$prereqsResourceGroupName = "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation"

	$dataKeyVaultName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
	$prereqsKeyVaultName = "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation"

	# Start with iterating through key vaults to remove role assignments that weren't assigned by Bicep
	$dataKeyVault = Get-AzKeyVault -ResourceGroupName $dataResourceGroupName -VaultName $dataKeyVaultName
	$prereqsKeyVault = Get-AzKeyVault -ResourceGroupName $prereqsResourceGroupName -VaultName $prereqsKeyVaultName

    $ScriptsDirectory = Split-Path $PSScriptRoot -Parent
	. ($ScriptsDirectory + '/PreDeploymentMigrations/Generate-BicepRoleAssignmentGuid.ps1')
	
	# Check each key vault, validate role assignments for all web apps
	foreach ($keyVault in @($dataKeyVault, $prereqsKeyVault)) {
		Write-Host "Checking Key Vault: $($keyVault.VaultName)"

		$roleAssignments = Get-AzRoleAssignment -Scope $keyVault.ResourceId

		$webAppRoleAssignments = $roleAssignments | Where-Object {
			$allWebapps.Identity.PrincipalId -contains $_.ObjectId -and
			$_.Scope -eq $keyVault.ResourceId
		}

		if ($webAppRoleAssignments -ne $null -and $webAppRoleAssignments.Count -gt 0 ) {
			Process-RoleAssignments -ResourceName $keyVault.VaultName `
			-ResourceId $keyVault.ResourceId `
			-RoleAssignments $webAppRoleAssignments `
			-SkipConfirmation:$SkipConfirmation
		}
		else {
			Write-Host "  No role assignments found for function apps on this key vault." -ForegroundColor Green
		}
	}

	# For each function app, we need to validate the storage account role assignments
	$functionApps = $allWebapps | Where-Object { $_.Kind -like "*functionapp*" }

	foreach($functionApp in $functionApps) {
		$functionAppName = $functionApp.Name
		Write-Host "Checking Function App: $functionAppName"
		$sizeIdentifier = ($functionAppName -split "-")[-1]
		if ($sizeIdentifier -notin @("large", "medium", "small", "onboarding", "s1", "m2", "l1", "o1")) {
			$sizeIdentifier = ""
		}
		$functionAbbreviation = ($functionAppName | Select-String -CaseSensitive -AllMatches -Pattern '[A-Z]').Matches.Value -join ''
		$prefix = $functionAbbreviation + $SolutionAbbreviation + $EnvironmentAbbreviation + "prod" + $sizeIdentifier
		if($prefix.Length -gt 23) {
			$prefix = $prefix.Substring(0, 23)
		}

		$allFunctionStorageAccounts = Get-AzStorageAccount -ResourceGroupName $dataResourceGroupName |
			Where-Object { $_.StorageAccountName -like "$prefix*" }

		if ($allFunctionStorageAccounts.Count -eq 0) {
			Write-Warning "No storage account found starting with '$prefix'. Skipping..."
			continue
		}

		$functionStorageAccount = $allFunctionStorageAccounts[0]

		# Now check role assignments on the storage account
		Write-Host "Checking Storage Account: $($functionStorageAccount.StorageAccountName)"
		$storageRoleAssignments = Get-AzRoleAssignment -Scope $functionStorageAccount.Id -PrincipalId $functionApp.Identity.PrincipalId
		$storageFunctionRoleAssignments = $storageRoleAssignments | Where-Object {
			$_.Scope -eq $functionStorageAccount.Id
		}

		if ($storageFunctionRoleAssignments -ne $null -and $storageFunctionRoleAssignments.Count -ne 0) {
			Process-RoleAssignments -ResourceName $functionStorageAccount.StorageAccountName `
			-ResourceId $functionStorageAccount.Id `
			-RoleAssignments $storageFunctionRoleAssignments `
			-SkipConfirmation:$SkipConfirmation
		}
		else {
			Write-Host "  No role assignments found for this function app on the storage account." -ForegroundColor Green
		}
	}

	Write-Verbose "Remove-FunctionScriptRBACRoles completed."
}