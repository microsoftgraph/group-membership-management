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

.PARAMETER FunctionAppName
Function app name [Optional: default will run through all 8 functions listed in README]

.PARAMETER AppConfigName
App config name.

.PARAMETER ErrorActionPreference
Parameter description

.EXAMPLE
Set-AppConfigurationManagedIdentityRoles  -SolutionAbbreviation "gmm" `
                                    -EnvironmentAbbreviation "<env>" `
                                    -AppConfigName "<app configuration name>" `
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
		[string] $FunctionAppName,
		[Parameter(Mandatory = $False)]
		[string] $AppConfigName,
		[Parameter(Mandatory = $False)]
		[string] $ErrorActionPreference = $Stop
	)

	$functionApps = @("GraphUpdater","MembershipAggregator","SecurityGroup","AzureTableBackup","AzureUserReader","JobScheduler","JobTrigger","NonProdService")

	$resourceGroupName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation";

	foreach ($functionApp in $functionApps)
	{
		Write-Host "Granting app service access to app configuration";

		$FunctionAppName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-$functionApp"

		Write-Host "FunctionAppName: $FunctionAppName"

		$appServicePrincipal = Get-AzADServicePrincipal -DisplayName $FunctionAppName;

		# Grant the app service access to the app configuration
		if (![string]::IsNullOrEmpty($AppConfigName))
		{
			$appConfigObject = Get-AzAppConfigurationStore -ResourceGroupName $resourceGroupName -Name $AppConfigName;

			if ($null -eq (Get-AzRoleAssignment -ObjectId $appServicePrincipal.Id -Scope $appConfigObject.Id))
			{
				New-AzRoleAssignment -ObjectId $appServicePrincipal.Id -Scope $appConfigObject.Id -RoleDefinitionName "App Configuration Data Reader";
				Write-Host "Added role assignment to allow $FunctionAppName to read from the $AppConfigName app configuration.";
			}
			else
			{
				Write-Host "$FunctionAppName can already read keys from the $AppConfigName app configuration.";
			}
		}

		if ([string]::IsNullOrEmpty($AppConfigName))
		{
			Write-Host "No app configuration was provided."
		}

		Write-Host "Done.";
	}
}
