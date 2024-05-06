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
Set-KeyVaultAccessRoles	-SolutionAbbreviation -SolutionAbbreviation <solution>" `
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
		[string] $ErrorActionPreference = $Stop
	)

	$functionApps = Get-AzFunctionApp -ResourceGroupName $ComputeResourceGroupName

	foreach ($functionApp in $functionApps) {

		Write-Host "Granting app service access to keyvaults";

		if ([string]::IsNullOrEmpty($DataResourceGroupName)) {
			$DataResourceGroupName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation";
		}

		if ([string]::IsNullOrEmpty($PrereqsResourceGroupName)) {
			$PrereqsResourceGroupName = "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation";
		}

		$ProductionFunctionAppName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-$functionApp"
		$StagingFunctionAppName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-$functionApp/slots/staging"
		$functionAppBasedOnSlots = @($ProductionFunctionAppName, $StagingFunctionAppName)

		$prereqsKeyVault = Get-AzKeyVault -ResourceGroupName $PrereqsResourceGroupName -Name "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation"
		$dataKeyVault = Get-AzKeyVault -ResourceGroupName $DataResourceGroupName -Name "$SolutionAbbreviation-data-$EnvironmentAbbreviation"

		foreach ($fa in $functionAppBasedOnSlots) {
			$appServicePrincipal = Get-AzADServicePrincipal -DisplayName $fa;

			# Grant the app service access to the keyvaults
			if ($appServicePrincipal) {

				# prereqs keyvault
				if ($null -eq (Get-AzRoleAssignment -ObjectId $functionServicePrincipal.Id -Scope $prereqsKeyVault.Id -RoleDefinitionName "Key Vault Secrets User")) {
					New-AzRoleAssignment -ObjectId $functionServicePrincipal.Id -Scope $prereqsKeyVault.Id -RoleDefinitionName "Key Vault Secrets User";
					Write-Host "Added role 'Key Vault Secrets User' to $($functionApp.Name) on the $($prereqsKeyVault.Name) keyvault.";
				}
				else {
					Write-Host "$($functionApp.Name) already has 'Key Vault Secrets User' role on $($prereqsKeyVault.Name).";
				}

				# data keyvault
				if ($null -eq (Get-AzRoleAssignment -ObjectId $functionServicePrincipal.Id -Scope $dataKeyVault.Id -RoleDefinitionName "Key Vault Secrets User")) {
					New-AzRoleAssignment -ObjectId $functionServicePrincipal.Id -Scope $dataKeyVault.Id -RoleDefinitionName "Key Vault Secrets User";
					Write-Host "Added role 'Key Vault Secrets User' to $($functionApp.Name) on the $($dataKeyVault.Name) keyvault.";
				}
				else {
					Write-Host "$($functionApp.Name) already has 'Key Vault Secrets User' role on $($dataKeyVault.Name).";
				}
			}
			elseif ($null -eq $appServicePrincipal) {
				Write-Host "Function $fa was not found!"
			}
		}
	}

	Write-Host "Done attempting to add keyvault role assignments.";
}