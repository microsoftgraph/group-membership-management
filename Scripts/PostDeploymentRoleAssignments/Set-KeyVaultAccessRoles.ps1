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

	$scriptsDirectory = Split-Path $PSScriptRoot -Parent
	. ($scriptsDirectory + '/ReusableModules/Invoke-WithRetry.ps1')

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

	$prereqsKeyVault = Invoke-WithRetry -Operation {
		Get-AzKeyVault -ResourceGroupName $PrereqsResourceGroupName -Name "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation"
	} -OperationName "Get prereqs key vault"

	$dataKeyVault = Invoke-WithRetry -Operation {
		Get-AzKeyVault -ResourceGroupName $DataResourceGroupName -Name "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
	} -OperationName "Get data key vault"

	$functionApps = Invoke-WithRetry -Operation {
		Get-AzWebApp -ResourceGroupName $ComputeResourceGroupName
	} -OperationName "List web apps for key vault role assignment"

	$serviceConnectionName = "$SolutionAbbreviation-serviceconnection-$EnvironmentAbbreviation"
	$serviceConnectionPrincipal = Invoke-WithRetry -Operation {
		Get-AzADServicePrincipal -DisplayName $serviceConnectionName
	} -OperationName "Get service connection service principal"
	if ($serviceConnectionPrincipal) {
		# prereqs keyvault
		Set-KVRoleAssignment `
			-ObjectId $serviceConnectionPrincipal.Id `
			-DisplayName $serviceConnectionName `
			-Scope $prereqsKeyVault.ResourceId `
			-RoleDefinitionName "Key Vault Secrets User" `
			-KeyVaultName $prereqsKeyVault.VaultName

		# data keyvault
		Set-KVRoleAssignment `
			-ObjectId $serviceConnectionPrincipal.Id `
			-DisplayName $serviceConnectionName `
			-Scope $dataKeyVault.ResourceId `
			-RoleDefinitionName "Key Vault Secrets User" `
			-KeyVaultName $dataKeyVault.VaultName
	}
	else {
		Write-Host "Service connection $serviceConnectionName was not found!"
	}

	# Grant the Function Apps access to the keyvaults
	foreach ($functionApp in $functionApps) {
		$functionAppName = $functionApp.Name

		$functionServicePrincipal = Invoke-WithRetry -Operation {
			Get-AzADServicePrincipal -DisplayName $functionAppName
		} -OperationName "Get function app service principal [$functionAppName]"

		# Grant the app service access to the keyvaults
		if ($functionServicePrincipal) {
			# prereqs keyvault
			Set-KVRoleAssignment `
				-ObjectId $functionServicePrincipal.Id `
				-DisplayName $functionAppName `
				-Scope $prereqsKeyVault.ResourceId `
				-RoleDefinitionName "Key Vault Secrets User" `
				-KeyVaultName $prereqsKeyVault.VaultName

			# data keyvault
			Set-KVRoleAssignment `
				-ObjectId $functionServicePrincipal.Id `
				-DisplayName $functionAppName `
				-Scope $dataKeyVault.ResourceId `
				-RoleDefinitionName "Key Vault Secrets User" `
				-KeyVaultName $dataKeyVault.VaultName
		}
		else {
			Write-Host "Function $functionAppName was not found!"
		}
	}

	# Grant the Web API access to the keyvaults
	$webApi = Invoke-WithRetry -Operation {
		Get-AzWebApp -ResourceGroupName $ComputeResourceGroupName -Name "$ComputeResourceGroupName-webapi"
	} -OperationName "Get web api app for key vault roles"
	if ($webApi) {
		$webApiServicePrincipal = Invoke-WithRetry -Operation {
			Get-AzADServicePrincipal -DisplayName $webApi.Name
		} -OperationName "Get web api service principal"

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
	$dataFactories = Invoke-WithRetry -Operation {
		Get-AzDataFactoryV2 -ResourceGroupName $DataResourceGroupName
	} -OperationName "List data factories for key vault roles"
	foreach ($dataFactory in $dataFactories) {
		$dataFactoryName = $dataFactory.DataFactoryName
		$dataFactoryServicePrincipal = $dataFactory.Identity.PrincipalId

		if ($dataFactoryServicePrincipal) {
			# data keyvault
			Set-KVRoleAssignment `
				-ObjectId $dataFactoryServicePrincipal `
				-DisplayName $dataFactoryName `
				-Scope $dataKeyVault.ResourceId `
				-RoleDefinitionName "Key Vault Secrets User" `
				-KeyVaultName $dataKeyVault.VaultName
		}
		else {
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

	$scriptsDirectory = Split-Path $PSScriptRoot -Parent
	. ($scriptsDirectory + '/ReusableModules/Invoke-WithRetry.ps1')

	$roleAssignment = Invoke-WithCreateRetry `
		-GetExistingOperation {
			Get-AzRoleAssignment -ObjectId $ObjectId -Scope $Scope -RoleDefinitionName $RoleDefinitionName -ErrorAction SilentlyContinue
		} `
		-CreateOperation {
			New-AzRoleAssignment -ObjectId $ObjectId -Scope $Scope -RoleDefinitionName $RoleDefinitionName
		} `
		-OperationName "Assign $RoleDefinitionName to $DisplayName" `
		-ExistsMessage "Role '$RoleDefinitionName' is already assigned to '$DisplayName' on '$KeyVaultName'. Skipping."

	if ($null -ne $roleAssignment) {
		Write-Host "Added or confirmed role $RoleDefinitionName for $DisplayName on the $KeyVaultName keyvault.";
	}
	else {
		Write-Host "$DisplayName already has  $RoleDefinitionName role on $KeyVaultName.";
	}
}