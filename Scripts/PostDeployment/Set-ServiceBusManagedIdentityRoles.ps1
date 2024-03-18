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

	Write-Host "Granting app service access to service bus queue and/or topic";

	if ([string]::IsNullOrEmpty($DataResourceGroupName)) {
		$DataResourceGroupName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation";
	}

	if ([string]::IsNullOrEmpty($ComputeResourceGroupName)) {
		$ComputeResourceGroupName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation";
	}

	$serviceBusNamespace = Get-AzServiceBusNamespace `
		-ResourceGroupName $DataResourceGroupName `
		-Name "$SolutionAbbreviation-data-$EnvironmentAbbreviation";

	$functionApps = Get-AzFunctionApp -ResourceGroupName $ComputeResourceGroupName

	foreach ($functionApp in $functionApps) {
		$functionServicePrincipal = Get-AzADServicePrincipal -DisplayName $functionApp.Name

		if ($null -eq (Get-AzRoleAssignment -ObjectId $functionServicePrincipal.Id -Scope $serviceBusNamespace.Id -RoleDefinitionName "Azure Service Bus Data Sender")) {
			New-AzRoleAssignment -ObjectId $functionServicePrincipal.Id -Scope $serviceBusNamespace.Id -RoleDefinitionName "Azure Service Bus Data Sender";
			Write-Host "Added role assignment to allow $($functionApp.Name) to send on the $($serviceBusNamespace.Name) namespace.";
		}
		else {
			Write-Host "$($functionApp.Name) can already send messages to the $($serviceBusNamespace.Name) queue.";
		}

		if ($null -eq (Get-AzRoleAssignment -ObjectId $functionServicePrincipal.Id -Scope $serviceBusNamespace.Id -RoleDefinitionName "Azure Service Bus Data Receiver")) {
			New-AzRoleAssignment -ObjectId $functionServicePrincipal.Id -Scope $serviceBusNamespace.Id -RoleDefinitionName "Azure Service Bus Data Receiver";
			Write-Host "Added role assignment to allow $($functionApp.Name) to receive on the $($serviceBusNamespace.Name) namespace.";
		}
		else {
			Write-Host "$($functionApp.Name) can already receive messages from the $($serviceBusNamespace.Name) queue.";
		}
	}

	Write-Host "Done.";
}