<#
.SYNOPSIS
Adds the app service's managed service identity as a reader on the app configuration.

.DESCRIPTION
Adds the app service's managed service identity as a reader on the app configuration so we don't need connection strings as much.
This should be run by an owner on the subscription after the app configuration and app service have been set up.
This should only have to be run once.

.PARAMETER SolutionAbbreviation
The abbreviation for your solution.

.PARAMETER EnvironmentAbbreviation
A 2-6 character abbreviation for your environment.

.PARAMETER ErrorActionPreference
Parameter description

.EXAMPLE
Set-AppConfigurationManagedIdentityRoles  	-SolutionAbbreviation "gmm" `
											-EnvironmentAbbreviation "<env>" `
											-Verbose
#>
function Set-AppConfigurationManagedIdentityRoles
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
	$apps = Invoke-WithRetry `
		-Operation { Get-AzWebApp -ResourceGroupName $computeResourceGroupName | Select-Object -ExpandProperty Name } `
		-OperationName "Get web apps in $computeResourceGroupName" `
		-MaxAttempts 3 -BaseDelaySeconds 2

	$resourceGroupName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation";
	if($DataResourceGroupName)
	{
		$resourceGroupName = $DataResourceGroupName
	}

	$appConfigName = "$SolutionAbbreviation-appConfig-$EnvironmentAbbreviation"
	$appConfigObject = Invoke-WithRetry `
		-Operation { Get-AzAppConfigurationStore -ResourceGroupName $resourceGroupName -Name $appConfigName } `
		-OperationName "Get App Configuration '$appConfigName'" `
		-MaxAttempts 3 -BaseDelaySeconds 2

	foreach ($appName in $apps)
	{
		Write-Host "Granting app service access to app configuration";
		Write-Host "FunctionAppName: $appName"

		$appServicePrincipal = Invoke-WithRetry `
			-Operation { Get-AzADServicePrincipal -DisplayName $appName } `
			-OperationName "Get service principal '$appName'" `
			-MaxAttempts 3 -BaseDelaySeconds 2

		# Grant the app service access to the app configuration
		if ($appServicePrincipal)
		{
			Invoke-WithCreateRetry `
				-GetExistingOperation { Get-AzRoleAssignment -ObjectId $appServicePrincipal.Id -Scope $appConfigObject.Id -RoleDefinitionName "App Configuration Data Reader" } `
				-CreateOperation {
					New-AzRoleAssignment -ObjectId $appServicePrincipal.Id -Scope $appConfigObject.Id -RoleDefinitionName "App Configuration Data Reader"
					Write-Host "Added role assignment to allow $appName to read from the $appConfigName app configuration."
				} `
				-OperationName "Assign App Config Reader to $appName" `
				-MaxAttempts 3 -BaseDelaySeconds 2 `
				-ExistsMessage "App Configuration Reader role is already assigned to '$appName'. Skipping."
		} elseif ($null -eq $appServicePrincipal) {
			Write-Host "App $appName was not found!"
		}
	}

	Write-Host "Done attempting to add App Configuration Data Reader role assignments.";
}
