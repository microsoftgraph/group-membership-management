<#
.SYNOPSIS
Adds the app service's managed service identity as a storage blob data contributor on the specified storage account.

.DESCRIPTION
Adds the app service's managed service identity as a storage blob data contributor on the specified storage account so we don't need connection strings as much.
This should be run by an owner on the subscription after the storage account and app service have been set up.
This should only have to be run once per function app.

.PARAMETER SolutionAbbreviation
The abbreviation for your solution.

.PARAMETER EnvironmentAbbreviation
A 2-6 character abbreviation for your environment.

.PARAMETER ErrorActionPreference
Parameter description

.EXAMPLE
Set-StorageAccountContainerManagedIdentityRoles	-SolutionAbbreviation "gmm" `
												-EnvironmentAbbreviation "<env>" `
												-Verbose
#>

function Set-StorageAccountContainerManagedIdentityRoles
{
	[CmdletBinding()]
	param(
		[Parameter(Mandatory = $True)]
		[string] $SolutionAbbreviation,
		[Parameter(Mandatory = $True)]
		[string] $EnvironmentAbbreviation,
		[Parameter(Mandatory = $False)]
		[string] $DataResourceGroupName = $null,
		[Parameter(Mandatory = $False)]
		[string] $ErrorActionPreference = $Stop
	)

	$scriptsDirectory = Split-Path $PSScriptRoot -Parent
	. ($scriptsDirectory + '/ReusableModules/Invoke-WithRetry.ps1')

	$computeResourceGroupName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation"
	$functionApps = Invoke-WithRetry `
		-Operation { Get-AzWebApp -ResourceGroupName $computeResourceGroupName | Select-Object -ExpandProperty Name } `
		-OperationName "Get web apps in $computeResourceGroupName" `
		-MaxAttempts 3 -BaseDelaySeconds 2

	$resourceGroupName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
	if($DataResourceGroupName)
	{
		$resourceGroupName = $DataResourceGroupName
	}

	$sharedFnMatches = Invoke-WithRetry `
		-Operation {
			Get-AzStorageAccount -ResourceGroupName $resourceGroupName |
				Where-Object { $_.StorageAccountName -like "fn$SolutionAbbreviation$EnvironmentAbbreviation*" }
		} `
		-OperationName "Get shared functions storage account" `
		-MaxAttempts 3 -BaseDelaySeconds 2

	$sharedFunctionsStorageAccount = $null
	if (-not $sharedFnMatches) {
		Write-Warning "No shared functions storage account found matching prefix 'fn$SolutionAbbreviation$EnvironmentAbbreviation'. Skipping shared functions SA role assignments."
	}
	elseif (@($sharedFnMatches).Count -gt 1) {
		Write-Warning "Multiple storage accounts found matching prefix 'fn$SolutionAbbreviation$EnvironmentAbbreviation'. Using $(@($sharedFnMatches)[0].StorageAccountName)."
		$sharedFunctionsStorageAccount = @($sharedFnMatches)[0]
	}
	else {
		$sharedFunctionsStorageAccount = @($sharedFnMatches)[0]
	}

	$jobsStorageAccount = Invoke-WithRetry `
		-Operation { Get-AzStorageAccount -ResourceGroupName $resourceGroupName | Where-Object { $_.StorageAccountName -like "jobs$EnvironmentAbbreviation*" } } `
		-OperationName "Get jobs storage account" `
		-MaxAttempts 3 -BaseDelaySeconds 2

	if (-not $jobsStorageAccount) {
		Write-Warning "No jobs storage account found matching prefix 'jobs$EnvironmentAbbreviation'. Skipping jobs SA role assignments."
	}

	foreach ($functionAppName in $functionApps)
	{

		Write-Host "Granting app service access to storage account blobs $functionAppName...";

		$appServicePrincipal = Invoke-WithRetry `
			-Operation { Get-AzADServicePrincipal -DisplayName $functionAppName } `
			-OperationName "Get service principal '$functionAppName'" `
			-MaxAttempts 3 -BaseDelaySeconds 2

		# Grant the app service access to the storage account blobs
		if ($appServicePrincipal)
		{
			if ($sharedFunctionsStorageAccount) {
				$functionStorageAccountRoles = @("Storage Queue Data Contributor","Storage Table Data Contributor","Storage Blob Data Contributor")

				foreach($role in $functionStorageAccountRoles)
				{
					Invoke-WithCreateRetry `
						-GetExistingOperation { Get-AzRoleAssignment -ObjectId $appServicePrincipal.Id -Scope $sharedFunctionsStorageAccount.Id -RoleDefinitionName $role } `
						-CreateOperation {
							New-AzRoleAssignment -ObjectId $appServicePrincipal.Id -Scope $sharedFunctionsStorageAccount.Id -RoleDefinitionName $role
							Write-Host "Added role assignment $role to $functionAppName with scope $($sharedFunctionsStorageAccount.Id)."
						} `
						-OperationName "Assign $role to $functionAppName (shared functions SA)" `
						-MaxAttempts 3 -BaseDelaySeconds 2 `
						-ExistsMessage "Role '$role' is already assigned to '$functionAppName' on shared functions SA. Skipping."
				}
			}

			if ($jobsStorageAccount) {
				$jobsStorageAccountRoles = @("Storage Blob Data Contributor")

				foreach($role in $jobsStorageAccountRoles)
				{
					Invoke-WithCreateRetry `
						-GetExistingOperation { Get-AzRoleAssignment -ObjectId $appServicePrincipal.Id -Scope $jobsStorageAccount.Id -RoleDefinitionName $role } `
						-CreateOperation {
							New-AzRoleAssignment -ObjectId $appServicePrincipal.Id -Scope $jobsStorageAccount.Id -RoleDefinitionName $role
							Write-Host "Added role assignment $role to $functionAppName with scope $($jobsStorageAccount.Id)."
						} `
						-OperationName "Assign $role to $functionAppName (jobs)" `
						-MaxAttempts 3 -BaseDelaySeconds 2 `
						-ExistsMessage "Role '$role' is already assigned to '$functionAppName' on jobs storage. Skipping."
				}
			}
		}
		elseif ($null -eq $appServicePrincipal) {
			Write-Host "Function $functionAppName was not found!"

		}
	}

	$webApiRoles = @("Storage Queue Data Contributor","Storage Table Data Contributor","Storage Blob Data Contributor")
	$webApi = Invoke-WithRetry `
		-Operation { Get-AzWebApp -ResourceGroupName "$SolutionAbbreviation-compute-$EnvironmentAbbreviation" -Name "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-webapi" } `
		-OperationName "Get WebAPI app" `
		-MaxAttempts 3 -BaseDelaySeconds 2
	$webApiSP = $webApi.Identity.PrincipalId
	$dataRG = Get-AzResourceGroup -Name "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
	$dataRGResourceId = $dataRG.ResourceId

	foreach($role in $webApiRoles)
	{
		Invoke-WithCreateRetry `
			-GetExistingOperation { Get-AzRoleAssignment -ObjectId $webApiSP -Scope $dataRGResourceId -RoleDefinitionName $role } `
			-CreateOperation {
				New-AzRoleAssignment -ObjectId $webApiSP -Scope $dataRGResourceId -RoleDefinitionName $role
				Write-Host "Added role assignment $role to $($webApi.Name) with scope $dataRGResourceId."
			} `
			-OperationName "Assign $role to WebAPI" `
			-MaxAttempts 3 -BaseDelaySeconds 2 `
			-ExistsMessage "Role '$role' is already assigned to WebAPI '$($webApi.Name)'. Skipping."
	}

	$serviceConnectionName = "$SolutionAbbreviation-serviceconnection-$EnvironmentAbbreviation"
	$serviceConnectionPrincipal = Invoke-WithRetry `
		-Operation { Get-AzADServicePrincipal -DisplayName $serviceConnectionName } `
		-OperationName "Get service connection principal" `
		-MaxAttempts 3 -BaseDelaySeconds 2
	if ($serviceConnectionPrincipal -and $jobsStorageAccount) {
		$serviceConnectionRoles = @("Storage Queue Data Contributor","Storage Table Data Contributor","Storage Blob Data Contributor")
		foreach($role in $serviceConnectionRoles)
		{
			Invoke-WithCreateRetry `
				-GetExistingOperation { Get-AzRoleAssignment -ObjectId $serviceConnectionPrincipal.Id -Scope $jobsStorageAccount.Id -RoleDefinitionName $role } `
				-CreateOperation {
					New-AzRoleAssignment -ObjectId $serviceConnectionPrincipal.Id -Scope $jobsStorageAccount.Id -RoleDefinitionName $role
					Write-Host "Added role assignment $role to $($serviceConnectionName) with scope $($jobsStorageAccount.Id)."
				} `
				-OperationName "Assign $role to service connection" `
				-MaxAttempts 3 -BaseDelaySeconds 2 `
				-ExistsMessage "Role '$role' is already assigned to service connection '$serviceConnectionName'. Skipping."
		}
	}
	elseif (-not $serviceConnectionPrincipal) {
		Write-Host "Service connection $($serviceConnectionName) was not found!"
	}

	Write-Host "Done attempting to add Storage role assignments.";
}
