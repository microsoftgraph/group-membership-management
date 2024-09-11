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

	$apps = @("WebApi","GraphUpdater","MembershipAggregator","GroupMembershipObtainer","SqlMembershipObtainer","PlaceMembershipObtainer","AzureMaintenance","AzureUserReader","JobScheduler","JobTrigger","NonProdService","Notifier","TeamsChannelMembershipObtainer","GroupOwnershipObtainer", "TeamsChannelUpdater", "DestinationAttributesUpdater", "SyncJobUpdater")

	$resourceGroupName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation";
	if($DataResourceGroupName)
	{
		$resourceGroupName = $DataResourceGroupName
	}

	$appConfigName = "$SolutionAbbreviation-appConfig-$EnvironmentAbbreviation"
	$appConfigObject = Get-AzAppConfigurationStore -ResourceGroupName $resourceGroupName -Name $appConfigName;

	foreach ($app in $apps)
	{
		Write-Host "Granting app service access to app configuration";

		$appName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-$app"

		Write-Host "FunctionAppName: $appName"

		$appServicePrincipal = Get-AzADServicePrincipal -DisplayName $appName;

		# Grant the app service access to the app configuration
		if ($appServicePrincipal)
		{

			if ($null -eq (Get-AzRoleAssignment -ObjectId $appServicePrincipal.Id -Scope $appConfigObject.Id -RoleDefinitionName "App Configuration Data Reader"))
			{
				$assignment = New-AzRoleAssignment -ObjectId $appServicePrincipal.Id -Scope $appConfigObject.Id -RoleDefinitionName "App Configuration Data Reader";
				if ($assignment) {
					Write-Host "Added role assignment to allow $appName to read from the $appConfigName app configuration.";
				}
				else {
					Write-Host "Failed to add role assignment to allow $appName to read from the $appConfigName app configuration. Please double check that you have permission to perform this operation";
				}
			}
			else
			{
				Write-Host "$appName can already read keys from the $appConfigName app configuration.";
			}
		} elseif ($null -eq $appServicePrincipal) {
			Write-Host "App $appName was not found!"
		}
	}

	Write-Host "Done attempting to add App Configuration Data Reader role assignments.";
}
