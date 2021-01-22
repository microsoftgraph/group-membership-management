<#
.SYNOPSIS
Adds an Azure active directory group as a writer on each service bus queue / topic.

.DESCRIPTION
Adds an Azure active directory group as a writer on each service bus queue / topic so we don't need connection strings as much. 
This should be run by an owner on the subscription after the service bus queues and app service have been set up.
This should only have to be run once.

.PARAMETER SolutionAbbreviation
The abbreviation for your solution.

.PARAMETER EnvironmentAbbreviation
A 2-6 character abbreviation for your environment.

.PARAMETER GroupName
Azure active directory group name

.PARAMETER QueueName
Queue name. Optional.

.PARAMETER TopicName
Topic name. Optional

.PARAMETER ErrorActionPreference
Parameter description

QueueName and TopicName are optionals but one must be provided.

.EXAMPLE
Set-Set-ServiceBusManagedIdentityRolesToADGroup  	-SolutionAbbreviation "gmm" `
                                    				-EnvironmentAbbreviation "<env>" `
                                    				-GroupName "<group name>" `
                                    				-QueueName "<queue name>" `
                                    				-TopicName "<topic name>" `
                                    				-Verbose
#>
function Set-Set-ServiceBusManagedIdentityRolesToADGroup 
{
	[CmdletBinding()]
	param(
		[Parameter(Mandatory = $True)]
		[string] $SolutionAbbreviation,
		[Parameter(Mandatory = $True)]
		[string] $EnvironmentAbbreviation,
		[Parameter(Mandatory = $True)]
		[string] $GroupName,        
		[Parameter(Mandatory = $False)]
		[string] $QueueName,
		[Parameter(Mandatory = $False)]
		[string] $TopicName,        
		[Parameter(Mandatory = $False)]
		[string] $ErrorActionPreference = $Stop
	)

	Write-Host "Granting $GroupName access to service bus queue and/or topic";

	$resourceGroupName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation";
	$ownerGroup = Get-AzADGroup -DisplayName $GroupName;
	
	if ($null -eq $ownerGroup) 
	{
		Write-Host "Group $GroupName was not found.";
		return;
	}

	# Grant the group access to the queue
	if (![string]::IsNullOrEmpty($QueueName)) 
	{
		$queueObject = Get-AzServiceBusQueue -ResourceGroupName $resourceGroupName -Namespace $resourceGroupName -Name $QueueName;

		if ($null -eq (Get-AzRoleAssignment -ObjectId $ownerGroup.Id -Scope $queueObject.Id)) 
		{
			New-AzRoleAssignment -ObjectId $ownerGroup.Id -Scope $queueObject.Id -RoleDefinitionName "Azure Service Bus Data Sender";
			Write-Host "Added role assignment to allow ${ownerGroup.DisplayName} to send on the $QueueName queue.";
		}
		else 
		{
			Write-Host "$($ownerGroup.DisplayName) can already send on the $QueueName queue.";    
		}
	}    
    
	# Grant the group access to the topic    
	if (![string]::IsNullOrEmpty($TopicName)) 
	{
		$topicObject = Get-AzServiceBusTopic -ResourceGroupName $resourceGroupName -Namespace $resourceGroupName -Name $TopicName;

		if ($null -eq (Get-AzRoleAssignment -ObjectId $ownerGroup.Id -Scope $topicObject.Id)) 
		{
			New-AzRoleAssignment -ObjectId $ownerGroup.Id -Scope $topicObject.Id -RoleDefinitionName "Azure Service Bus Data Sender";
			Write-Host "Added role assignment to allow ${ownerGroup.DisplayName} to send on the $TopicName topic.";
		}
		else 
		{
			Write-Host "$($ownerGroup.DisplayName) can already send on the $TopicName topic.";    
		}
	}

	if ([string]::IsNullOrEmpty($QueueName) -And [string]::IsNullOrEmpty($TopicName)) 
	{
		Write-Host "No queue or topic was provided."
	}

	Write-Host "Done.";
}
