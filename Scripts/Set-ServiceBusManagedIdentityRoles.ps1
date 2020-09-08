<#
.SYNOPSIS
Adds the app service's managed service identity as a writer on each service bus queue.

.DESCRIPTION
Adds the app service's managed service identity as a writer on each service bus queue so we don't need connection strings as much. 
This should be run by an owner on the subscription after the service bus queues and app service have been set up.
This should only have to be run once.

.PARAMETER SolutionAbbreviation
The abbreviation for your solution.

.PARAMETER EnvironmentAbbreviation
A 2-4 character abbreviation for your environment.

.PARAMETER ErrorActionPreference
Parameter description

.EXAMPLE
Set-ServiceBusManagedIdentityRoles  -SolutionAbbreviation "gmm" `
                                    -EnvironmentAbbreviation "<env>" `
                                    -AppServiceName "<app service name>" `
                                    -QueueName "<queue name>" `
                                    -TopicName "<topic name>" `
                                    -Verbose
#>
function Set-ServiceBusManagedIdentityRoles {
	[CmdletBinding()]
	param(
        [Parameter(Mandatory=$True)]
        [string] $SolutionAbbreviation,
        [Parameter(Mandatory=$True)]
        [string] $EnvironmentAbbreviation,
        [Parameter(Mandatory=$True)]
        [string] $AppServiceName,        
        [Parameter(Mandatory=$False)]
        [string] $QueueName,
        [Parameter(Mandatory=$False)]
        [string] $TopicName,        
        [Parameter(Mandatory=$False)]
        [string] $ErrorActionPreference = $Stop
	)

    Write-Host "Granting app service access to service bus queue and/or topic";

    $resourceGroupName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation";
    $appServicePrincipal = Get-AzADServicePrincipal -DisplayName $AppServiceName;
    
    if ($EnvironmentAbbreviation -like "prod")
    {
        $ownerGroup = Get-AzADGroup -DisplayName "EUSE ST GMM SWE Leads";
    }
    else 
    {
        $ownerGroup = Get-AzADGroup -DisplayName "EUSE ST GMM SWE Team";
    }
    
    # Grant the app service access to the queue
    if(![string]::IsNullOrEmpty($QueueName))
    {
        $queueObject = Get-AzServiceBusQueue -ResourceGroupName $resourceGroupName -Namespace $resourceGroupName -Name $QueueName;

        if ($null -eq (Get-AzRoleAssignment -ObjectId $appServicePrincipal.Id -Scope $queueObject.Id))
        {
            New-AzRoleAssignment -ObjectId $appServicePrincipal.Id -Scope $queueObject.Id -RoleDefinitionName "Azure Service Bus Data Sender";
            Write-Host "Added role assignment to allow $AppServiceName to send on the $QueueName queue.";
        }
        else
        {
            Write-Host "$AppServiceName can already send on the $QueueName queue.";
        }
    
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
    
    # Grant the app service access to the topic    
    if(![string]::IsNullOrEmpty($TopicName))
    {
        $topicObject = Get-AzServiceBusQueue -ResourceGroupName $resourceGroupName -Namespace $resourceGroupName -Name $TopicName;

        if ($null -eq (Get-AzRoleAssignment -ObjectId $appServicePrincipal.Id -Scope $topicObject.Id))
        {
            New-AzRoleAssignment -ObjectId $appServicePrincipal.Id -Scope $topicObject.Id -RoleDefinitionName "Azure Service Bus Data Sender";
            Write-Host "Added role assignment to allow $AppServiceName to send on the $TopicName topic.";
        }
        else
        {
            Write-Host "$AppServiceName can already send on the $TopicName topic.";    
        }
    
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

    if([string]::IsNullOrEmpty($QueueName) -And [string]::IsNullOrEmpty($TopicName)) {
        Write-Host "No queue or topic was provided."
    }

    Write-Host "Done.";
}