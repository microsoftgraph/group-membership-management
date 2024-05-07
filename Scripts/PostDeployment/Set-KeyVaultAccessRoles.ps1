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

	# Grant the Function Apps access to the keyvaults
	foreach ($functionApp in $functionApps) {
		$ProductionFunctionAppName = $functionApp.Name
		$StagingFunctionAppName = "$($functionApp.Name)/slots/staging"
		$functionAppBasedOnSlots = @($ProductionFunctionAppName, $StagingFunctionAppName)

		foreach ($fa in $functionAppBasedOnSlots) {
			$functionServicePrincipal = Get-AzADServicePrincipal -DisplayName $fa;

			# Grant the app service access to the keyvaults
			if ($functionServicePrincipal) {
				# prereqs keyvault
				Set-KVRoleAssignment `
				-ObjectId $functionServicePrincipal.Id `
				-DisplayName $fa `
				-Scope $prereqsKeyVault.ResourceId `
				-RoleDefinitionName "Key Vault Secrets User" `
				-KeyVaultName $prereqsKeyVault.VaultName

				# data keyvault
				Set-KVRoleAssignment `
				-ObjectId $functionServicePrincipal.Id `
				-DisplayName $fa `
				-Scope $dataKeyVault.ResourceId `
				-RoleDefinitionName "Key Vault Secrets User" `
				-KeyVaultName $dataKeyVault.VaultName
			}
			elseif ($null -eq $functionServicePrincipal) {
				Write-Host "Function $fa was not found!"
			}
		}
	}

	# Grant the Web API access to the keyvaults
	$webApi = Get-AzWebApp -ResourceGroupName $ComputeResourceGroupName -Name "$ComputeResourceGroupName-webapi"
	if ($webApi) {
		$webApiServicePrincipal = Get-AzADServicePrincipal -DisplayName $webApi.Name

		if ($webApiServicePrincipal) {
			# prereqs keyvault
			Set-KVRoleAssignment `
			-ObjectId $webApiServicePrincipal.Id `
			-DisplayName $webApi.Name `
			-Scope $prereqsKeyVault.ResourceId `
			-RoleDefinitionName "Key Vault Secrets User" `
			-KeyVaultName $prereqsKeyVault.VaultName

			# data keyvault
			Set-KVRoleAssignment `
			-ObjectId $webApiServicePrincipal.Id `
			-DisplayName $webApi.Name `
			-Scope $dataKeyVault.ResourceId `
			-RoleDefinitionName "Key Vault Secrets User" `
			-KeyVaultName $dataKeyVault.VaultName
		}
		elseif ($null -eq $webApiServicePrincipal) {
			Write-Host "Web API $($webApi.Name) was not found!"
		}
	}

	# Grant the Data Factories access to the keyvaults
	$dataFactories = Get-AzResource -ResourceGroupName $DataResourceGroupName -ResourceType "Microsoft.DataFactory/factories"
	foreach ($dataFactory in $dataFactories) {
		$dataFactoryName = $dataFactory.Name
		$dataFactoryServicePrincipal = Get-AzADServicePrincipal -DisplayName $dataFactoryName

		if ($dataFactoryServicePrincipal) {
			# data keyvault
			Set-KVRoleAssignment `
			-ObjectId $dataFactoryServicePrincipal.Id `
			-DisplayName $dataFactoryName `
			-Scope $dataKeyVault.ResourceId `
			-RoleDefinitionName "Key Vault Secrets User" `
			-KeyVaultName $dataKeyVault.VaultName
		}
		elseif ($null -eq $dataFactoryServicePrincipal) {
			Write-Host "Data Factory $dataFactoryName was not found!"
		}

	}

	Write-Host "Done attempting to add keyvault role assignments.";
}

function Set-KVRoleAssignment {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory = $True)]
		[string] $ObjectId,
		[Parameter(Mandatory = $True)]
		[string] $DisplayName,
		[Parameter(Mandatory = $True)]
		[string] $Scope,
		[Parameter(Mandatory = $True)]
		[string] $RoleDefinitionName,
		[Parameter(Mandatory = $True)]
		[string] $KeyVaultName
	)

	if ($null -eq (Get-AzRoleAssignment -ObjectId $ObjectId -Scope $Scope -RoleDefinitionName $RoleDefinitionName)) {
		New-AzRoleAssignment -ObjectId $ObjectId -Scope $Scope -RoleDefinitionName $RoleDefinitionName;
		Write-Host "Added role $RoleDefinitionName to $DisplayName on the $KeyVaultName keyvault.";
	}
	else {
		Write-Host "$DisplayName already has  $RoleDefinitionName role on $KeyVaultName.";
	}
}