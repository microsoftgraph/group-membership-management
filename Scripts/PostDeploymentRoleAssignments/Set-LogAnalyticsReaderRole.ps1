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
		[string] $DataResourceGroupName = $null,
		[Parameter(Mandatory = $False)]
		[string] $ErrorActionPreference = $Stop
	)

	$scriptsDirectory = Split-Path $PSScriptRoot -Parent
	. ($scriptsDirectory + '/ReusableModules/Invoke-WithRetry.ps1')

	$functionApps = @("JobScheduler")

	$resourceGroupName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation";
	if($DataResourceGroupName)
	{
		$resourceGroupName = $DataResourceGroupName
	}

	$logAnalyticsWorkspaceResourceName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation";

	foreach ($functionApp in $functionApps)
	{
		Write-Host "Granting app service access to Log Analytics resource";

		$functionAppName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-$functionApp"

		$appServicePrincipal = Invoke-WithRetry `
			-Operation { Get-AzADServicePrincipal -DisplayName $functionAppName } `
			-OperationName "Get service principal '$functionAppName'" `
			-MaxAttempts 3 -BaseDelaySeconds 2

		# Grant the app service access to the Log Analytics resource logs
		if ($appServicePrincipal)
		{
			$logAnalyticsObject = Invoke-WithRetry `
				-Operation { Get-AzOperationalInsightsWorkspace -ResourceGroupName $resourceGroupName -Name $logAnalyticsWorkspaceResourceName } `
				-OperationName "Get Log Analytics workspace" `
				-MaxAttempts 3 -BaseDelaySeconds 2

			Invoke-WithCreateRetry `
				-GetExistingOperation { Get-AzRoleAssignment -ObjectId $appServicePrincipal.Id -Scope $logAnalyticsObject.ResourceId } `
				-CreateOperation {
					New-AzRoleAssignment -ObjectId $appServicePrincipal.Id -Scope $logAnalyticsObject.ResourceId -RoleDefinitionName "Log Analytics Reader"
					Write-Host "Added role assignment to allow $functionAppName to access on the $logAnalyticsWorkspaceResourceName logs."
				} `
				-OperationName "Assign Log Analytics Reader to $functionAppName" `
				-MaxAttempts 3 -BaseDelaySeconds 2 `
				-ExistsMessage "Log Analytics Reader role is already assigned to '$functionAppName'. Skipping."
		}
		elseif ($null -eq $appServicePrincipal) {
			Write-Host "Function $functionAppName was not found!"
		}
	}

	Write-Host "Done attempting to add Log Analytics Reader role assignment(s).";
}