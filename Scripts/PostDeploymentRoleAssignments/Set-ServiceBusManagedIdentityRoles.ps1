<#
.SYNOPSIS
Adds the app service's managed service identity as a reader and sender on each service bus queue.

.DESCRIPTION
Adds the app service's managed service identity as a reader and sender on each service bus queue so we don't need connection strings as much. 
This should be run by an owner on the subscription after the service bus queues and app service have been set up.
This should only have to be run once.

.PARAMETER SolutionAbbreviation
The abbreviation for your solution.

.PARAMETER EnvironmentAbbreviation
A 2-6 character abbreviation for your environment.

.PARAMETER DataResourceGroupName
Optional.
The resource group name for the data resources.
If not provided, it will be inferred from the solution abbreviation and environment abbreviation.

.PARAMETER ErrorActionPreference
Parameter description

.EXAMPLE
Set-ServiceBusManagedIdentityRoles  -SolutionAbbreviation "<solution>" `
                                    -EnvironmentAbbreviation "<environment>" `
                                    -Verbose
#>
function Set-ServiceBusManagedIdentityRoles {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory = $True)]
		[string] $SolutionAbbreviation,
		[Parameter(Mandatory = $True)]
		[string] $EnvironmentAbbreviation,
		[Parameter(Mandatory = $False)]
		[string] $DataResourceGroupName,
		[Parameter(Mandatory = $False)]
		[string] $ComputeResourceGroupName,
		[Parameter(Mandatory = $False)]
		[string] $ErrorActionPreference = $Stop
	)

	$scriptsDirectory = Split-Path $PSScriptRoot -Parent
	. ($scriptsDirectory + '/ReusableModules/Invoke-WithRetry.ps1')

	Write-Host "Granting app service access to service bus queue and/or topic";

	if ([string]::IsNullOrEmpty($DataResourceGroupName)) {
		$DataResourceGroupName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation";
	}

	if ([string]::IsNullOrEmpty($ComputeResourceGroupName)) {
		$ComputeResourceGroupName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation";
	}

	$serviceBusNamespace = Invoke-WithRetry -Operation {
		Get-AzServiceBusNamespace `
			-ResourceGroupName $DataResourceGroupName `
			-Name "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
	} -OperationName "Get service bus namespace"

	$functionApps = Invoke-WithRetry -Operation {
		Get-AzWebApp -ResourceGroupName $ComputeResourceGroupName
	} -OperationName "List web apps for service bus role assignment"

	foreach ($functionApp in $functionApps) {
		$functionServicePrincipal = Invoke-WithRetry -Operation {
			Get-AzADServicePrincipal -DisplayName $functionApp.Name
		} -OperationName "Get function app service principal [$($functionApp.Name)]"

		Invoke-WithCreateRetry `
			-GetExistingOperation {
				Get-AzRoleAssignment -ObjectId $functionServicePrincipal.Id -Scope $serviceBusNamespace.Id -RoleDefinitionName "Azure Service Bus Data Sender" -ErrorAction SilentlyContinue
			} `
			-CreateOperation {
				New-AzRoleAssignment -ObjectId $functionServicePrincipal.Id -Scope $serviceBusNamespace.Id -RoleDefinitionName "Azure Service Bus Data Sender"
			} `
			-OperationName "Assign Service Bus Data Sender for $($functionApp.Name)" `
			-ExistsMessage "Service Bus Data Sender role is already assigned to '$($functionApp.Name)'. Skipping." | Out-Null

		Invoke-WithCreateRetry `
			-GetExistingOperation {
				Get-AzRoleAssignment -ObjectId $functionServicePrincipal.Id -Scope $serviceBusNamespace.Id -RoleDefinitionName "Azure Service Bus Data Receiver" -ErrorAction SilentlyContinue
			} `
			-CreateOperation {
				New-AzRoleAssignment -ObjectId $functionServicePrincipal.Id -Scope $serviceBusNamespace.Id -RoleDefinitionName "Azure Service Bus Data Receiver"
			} `
			-OperationName "Assign Service Bus Data Receiver for $($functionApp.Name)" `
			-ExistsMessage "Service Bus Data Receiver role is already assigned to '$($functionApp.Name)'. Skipping." | Out-Null

		Write-Host "$($functionApp.Name) can send/receive messages on the $($serviceBusNamespace.Name) namespace."
	}

	$webApi = Invoke-WithRetry -Operation {
		Get-AzWebApp -ResourceGroupName $ComputeResourceGroupName -Name "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-webapi"
	} -OperationName "Get web api app for service bus role assignment"
	$webApiSP = $webApi.Identity.PrincipalId

	Invoke-WithCreateRetry `
		-GetExistingOperation {
			Get-AzRoleAssignment -ObjectId $webApiSP -Scope $serviceBusNamespace.Id -RoleDefinitionName "Azure Service Bus Data Sender" -ErrorAction SilentlyContinue
		} `
		-CreateOperation {
			New-AzRoleAssignment -ObjectId $webApiSP -Scope $serviceBusNamespace.Id -RoleDefinitionName "Azure Service Bus Data Sender"
		} `
		-OperationName "Assign Service Bus Data Sender for $($webApi.Name)" `
		-ExistsMessage "Service Bus Data Sender role is already assigned to '$($webApi.Name)'. Skipping." | Out-Null

	Invoke-WithCreateRetry `
		-GetExistingOperation {
			Get-AzRoleAssignment -ObjectId $webApiSP -Scope $serviceBusNamespace.Id -RoleDefinitionName "Azure Service Bus Data Receiver" -ErrorAction SilentlyContinue
		} `
		-CreateOperation {
			New-AzRoleAssignment -ObjectId $webApiSP -Scope $serviceBusNamespace.Id -RoleDefinitionName "Azure Service Bus Data Receiver"
		} `
		-OperationName "Assign Service Bus Data Receiver for $($webApi.Name)" `
		-ExistsMessage "Service Bus Data Receiver role is already assigned to '$($webApi.Name)'. Skipping." | Out-Null

	Write-Host "$($webApi.Name) can send/receive messages on the $($serviceBusNamespace.Name) namespace."

	Write-Host "Done.";
}