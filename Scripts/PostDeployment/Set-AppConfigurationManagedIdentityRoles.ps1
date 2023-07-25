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
		[string] $ErrorActionPreference = $Stop
	)

	$functionApps = @("GraphUpdater","MembershipAggregator","GroupMembershipObtainer","AzureMaintenance","AzureUserReader","JobScheduler","JobTrigger","NonProdService","Notifier","TeamsChannel","GroupOwnershipObtainer", "TeamsChannelUpdater")
	$webApi = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-webapi"

	$resourceGroupName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation";
	$appConfigName = "$SolutionAbbreviation-appConfig-$EnvironmentAbbreviation"

	foreach ($functionApp in $functionApps)
	{
		Write-Host "Granting app service access to app configuration";

		$ProductionFunctionAppName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-$functionApp"
		$StagingFunctionAppName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-$functionApp/slots/staging"

		$functionAppBasedOnSlots = @($ProductionFunctionAppName,$StagingFunctionAppName, $webApi)

		foreach ($fa in $functionAppBasedOnSlots)
		{

			Write-Host "FunctionAppName: $fa"

			$appServicePrincipal = Get-AzADServicePrincipal -DisplayName $fa;

			# Grant the app service access to the app configuration
			if ($appServicePrincipal)
			{
				$appConfigObject = Get-AzAppConfigurationStore -ResourceGroupName $resourceGroupName -Name $appConfigName;

				if ($null -eq (Get-AzRoleAssignment -ObjectId $appServicePrincipal.Id -Scope $appConfigObject.Id))
				{
					$assignment = New-AzRoleAssignment -ObjectId $appServicePrincipal.Id -Scope $appConfigObject.Id -RoleDefinitionName "App Configuration Data Reader";
					if ($assignment) {
						Write-Host "Added role assignment to allow $fa to read from the $appConfigName app configuration.";
					}
					else {
						Write-Host "Failed to add role assignment to allow $fa to read from the $appConfigName app configuration. Please double check that you have permission to perform this operation";
					}
				}
				else
				{
					Write-Host "$fa can already read keys from the $appConfigName app configuration.";
				}
			} elseif ($null -eq $appServicePrincipal) {
				Write-Host "Function $fa was not found!"
			}
		}
	}

	Write-Host "Done attempting to add App Configuration Data Reader role assignments.";
}
