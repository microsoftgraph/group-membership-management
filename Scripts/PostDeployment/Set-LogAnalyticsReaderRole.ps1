<#
.SYNOPSIS
Adds the app service's managed service identity as a Log Analytics Reader on a Log Analytics resource.

.DESCRIPTION
Adds the app service's managed service identity as a Log Analytics Reader on a Log Analytics resource so we don't need connection strings as much.
This should be run by an owner on the subscription after the Log Analytics resource and app service have been set up.
This should only have to be run once per function app.

.PARAMETER SolutionAbbreviation
The abbreviation for your solution.

.PARAMETER EnvironmentAbbreviation
A 2-6 character abbreviation for your environment.

.PARAMETER ErrorActionPreference
Parameter description

.EXAMPLE
Set-LogAnalyticsReaderRole	-SolutionAbbreviation "gmm" `
							-EnvironmentAbbreviation "<env>" `
							-Verbose
#>

function Set-LogAnalyticsReaderRole
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

	$functionApps = @("JobScheduler", "Notifier")

	$resourceGroupName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation";
	$logAnalyticsWorkspaceResourceName = $resourceGroupName

	foreach ($functionApp in $functionApps)
	{
		Write-Host "Granting app service access to Log Analytics resource";

		$ProductionFunctionAppName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-$functionApp"
		$StagingFunctionAppName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-$functionApp/slots/staging"

		$functionAppBasedOnSlots = @($ProductionFunctionAppName,$StagingFunctionAppName)

		foreach ($fa in $functionAppBasedOnSlots)
		{
			$appServicePrincipal = Get-AzADServicePrincipal -DisplayName $fa;

			# Grant the app service access to the Log Analytics resource logs
			if ($appServicePrincipal)
			{
				$logAnalyticsObject = Get-AzOperationalInsightsWorkspace -ResourceGroupName $resourceGroupName -Name $logAnalyticsWorkspaceResourceName;

				if ($null -eq (Get-AzRoleAssignment -ObjectId $appServicePrincipal.Id -Scope $logAnalyticsObject.ResourceId))
				{
					$assignment = New-AzRoleAssignment -ObjectId $appServicePrincipal.Id -Scope $logAnalyticsObject.ResourceId -RoleDefinitionName "Log Analytics Reader";
					if ($assignment) {
						Write-Host "Added role assignment to allow $fa to access on the $logAnalyticsWorkspaceResourceName logs.";
					}
					else {
						Write-Host "Failed to add role assignment to allow $fa to access on the $logAnalyticsWorkspaceResourceName logs. Please double check that you have permission to perform this operation";
					}
				}
				else
				{
					Write-Host "$fa already has access to $logAnalyticsWorkspaceResourceName logs.";
				}
			}
			elseif ($null -eq $appServicePrincipal) {
				Write-Host "Function $fa was not found!"
			}
		}
	}

	Write-Host "Done attempting to add Log Analytics Reader role assignment(s).";
}