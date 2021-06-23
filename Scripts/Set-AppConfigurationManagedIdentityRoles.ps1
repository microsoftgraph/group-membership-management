<#
.SYNOPSIS
Adds the app service's managed service identity as a reader on the app configuration.

.DESCRIPTION
Adds the app service's managed service identity as a reader on each service bus queue so we don't need connection strings as much. 
This should be run by an owner on the subscription after the app configuration and app service have been set up.
This should only have to be run once.

.PARAMETER SolutionAbbreviation
The abbreviation for your solution.

.PARAMETER EnvironmentAbbreviation
A 2-6 character abbreviation for your environment.

.PARAMETER FunctionAppName
Function app name

.PARAMETER AppConfigName
App config name.

.PARAMETER ErrorActionPreference
Parameter description

QueueName and TopicName are optionals but one must be provided.

.EXAMPLE
Set-ServiceBusManagedIdentityRoles  -SolutionAbbreviation "gmm" `
                                    -EnvironmentAbbreviation "<env>" `
                                    -FunctionAppName "<function app name>" `
                                    -AppConfigName "<app configuration name>" `
                                    -Verbose
#>
function Set-ServiceBusManagedIdentityRoles 
{
	[CmdletBinding()]
	param(
		[Parameter(Mandatory = $True)]
		[string] $SolutionAbbreviation,
		[Parameter(Mandatory = $True)]
		[string] $EnvironmentAbbreviation,
		[Parameter(Mandatory = $True)]
		[string] $FunctionAppName,        
		[Parameter(Mandatory = $False)]
		[string] $AppConfigName,
		[Parameter(Mandatory = $False)]
		[string] $ErrorActionPreference = $Stop
	)

	Write-Host "Granting app service access to app configuration";

	$resourceGroupName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation";
	$appServicePrincipal = Get-AzADServicePrincipal -DisplayName $FunctionAppName;
    
	# Grant the app service access to the queue
	if (![string]::IsNullOrEmpty($QueueName)) 
	{
		$appConfigObject = Get-AzAppConfigurationStore -ResourceGroupName $resourceGroupName -Name $AppConfigName;

		if ($null -eq (Get-AzRoleAssignment -ObjectId $appServicePrincipal.Id -Scope $appConfigObject.Id)) 
		{
			New-AzRoleAssignment -ObjectId $appServicePrincipal.Id -Scope $appConfigObject.Id -RoleDefinitionName "App Configuration Data Reader";
			Write-Host "Added role assignment to allow $FunctionAppName to send on the $AppConfigName app configuration.";
		}
		else 
		{
			Write-Host "$FunctionAppName can already read keys from the $AppConfigName app configuration.";
		}
	}

	if ([string]::IsNullOrEmpty($AppConfigName) -And [string]::IsNullOrEmpty($TopicName)) 
	{
		Write-Host "No app configuration was provided."
	}

	Write-Host "Done.";
}
