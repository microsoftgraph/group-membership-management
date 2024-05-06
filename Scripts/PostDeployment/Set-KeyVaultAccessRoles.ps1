<#
.SYNOPSIS
Grants access to prereqs and data keyvaults

.DESCRIPTION
This should be run by an owner on the subscription after the storage account and app service have been set up.
This should only have to be run once per function app.

.PARAMETER SolutionAbbreviation
The abbreviation for your solution.

.PARAMETER EnvironmentAbbreviation
A 2-6 character abbreviation for your environment.

.PARAMETER ErrorActionPreference
Parameter description

.EXAMPLE
Set-KeyVaultAccessRoles	-SolutionAbbreviation "<solution>" `
						-EnvironmentAbbreviation "<env>" `
						-Verbose
#>

function Set-KeyVaultAccessRoles {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory = $True)]
		[string] $SolutionAbbreviation,
		[Parameter(Mandatory = $True)]
		[string] $EnvironmentAbbreviation,
		[Parameter(Mandatory = $False)]
		[string] $PrereqsResourceGroupName = $null,
		[Parameter(Mandatory = $False)]
		[string] $DataResourceGroupName = $null,
		[Parameter(Mandatory = $False)]
		[string] $ComputeResourceGroupName = $null,
		[Parameter(Mandatory = $False)]
		[string] $ErrorActionPreference = $Stop
	)

	Write-Host "Granting app service access to keyvaults";

	if ([string]::IsNullOrEmpty($ComputeResourceGroupName)) {
		$ComputeResourceGroupName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation";
	}

	if ([string]::IsNullOrEmpty($DataResourceGroupName)) {
		$DataResourceGroupName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation";
	}

	if ([string]::IsNullOrEmpty($PrereqsResourceGroupName)) {
		$PrereqsResourceGroupName = "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation";
	}

	$prereqsKeyVault = Get-AzKeyVault -ResourceGroupName $PrereqsResourceGroupName -Name "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation"
	$dataKeyVault = Get-AzKeyVault -ResourceGroupName $DataResourceGroupName -Name "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
	$functionApps = Get-AzFunctionApp -ResourceGroupName $ComputeResourceGroupName

	foreach ($functionApp in $functionApps) {
		$ProductionFunctionAppName = $functionApp.Name
		$StagingFunctionAppName = "$($functionApp.Name)/slots/staging"
		$functionAppBasedOnSlots = @($ProductionFunctionAppName, $StagingFunctionAppName)

		foreach ($fa in $functionAppBasedOnSlots) {
			$functionServicePrincipal = Get-AzADServicePrincipal -DisplayName $fa;

			# Grant the app service access to the keyvaults
			if ($functionServicePrincipal) {
				# prereqs keyvault
				if ($null -eq (Get-AzRoleAssignment -ObjectId $functionServicePrincipal.Id -Scope $prereqsKeyVault.ResourceId -RoleDefinitionName "Key Vault Secrets User")) {
					New-AzRoleAssignment -ObjectId $functionServicePrincipal.Id -Scope $prereqsKeyVault.ResourceId -RoleDefinitionName "Key Vault Secrets User";
					Write-Host "Added role 'Key Vault Secrets User' to $fa on the $($prereqsKeyVault.VaultName) keyvault.";
				}
				else {
					Write-Host "$fa already has 'Key Vault Secrets User' role on $($prereqsKeyVault.VaultName).";
				}

				# data keyvault
				if ($null -eq (Get-AzRoleAssignment -ObjectId $functionServicePrincipal.Id -Scope $dataKeyVault.ResourceId -RoleDefinitionName "Key Vault Secrets User")) {
					New-AzRoleAssignment -ObjectId $functionServicePrincipal.Id -Scope $dataKeyVault.ResourceId -RoleDefinitionName "Key Vault Secrets User";
					Write-Host "Added role 'Key Vault Secrets User' to $fa on the $($dataKeyVault.VaultName) keyvault.";
				}
				else {
					Write-Host "$fa already has 'Key Vault Secrets User' role on $($dataKeyVault.VaultName).";
				}
			}
			elseif ($null -eq $functionServicePrincipal) {
				Write-Host "Function $fa was not found!"
			}
		}
	}

	$dataFactories = Get-AzResource -ResourceGroupName $DataResourceGroupName -ResourceType "Microsoft.DataFactory/factories"
	foreach ($dataFactory in $dataFactories) {
		$dataFactoryName = $dataFactory.Name
		$dataFactoryServicePrincipal = Get-AzADServicePrincipal -DisplayName $dataFactoryName

		if ($dataFactoryServicePrincipal) {
			# data keyvault
			if ($null -eq (Get-AzRoleAssignment -ObjectId $dataFactoryServicePrincipal.Id -Scope $dataKeyVault.ResourceId -RoleDefinitionName "Key Vault Secrets User")) {
				New-AzRoleAssignment -ObjectId $dataFactoryServicePrincipal.Id -Scope $dataKeyVault.ResourceId -RoleDefinitionName "Key Vault Secrets User";
				Write-Host "Added role 'Key Vault Secrets User' to $($dataFactoryName) on the $($dataKeyVault.VaultName) keyvault.";
			}
			else {
				Write-Host "$($dataFactoryName) already has 'Key Vault Secrets User' role on $($dataKeyVault.VaultName).";
			}
		}
		elseif ($null -eq $dataFactoryServicePrincipal) {
			Write-Host "Data Factory $dataFactoryName was not found!"
		}

	}

	Write-Host "Done attempting to add keyvault role assignments.";
}