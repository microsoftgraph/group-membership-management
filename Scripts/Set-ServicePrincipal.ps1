$ErrorActionPreference = "Stop"
<# 
.SYNOPSIS
This script creates a service principal

.PARAMETER ServicePrincipalName
Name for the new service principal

.PARAMETER SolutionAbbreviation
Abbreviation used to denote the overall solution (or application)

.PARAMETER EnvironmentAbbreviation
Abbreviation for the environment

.EXAMPLE
Set-ServicePrincipal -ServicePrincipalName "<servicePrincipalName>" `
                     -SolutionAbbreviation "<solutionAbbreviation>" `
                     -EnvironmentAbbreviation "<environmentAbbreviation>"
#>

function Set-Role {
    Param(
        [Parameter (Mandatory=$true)]
        [string]
        $RoleDefinitionName,
        [Parameter (Mandatory=$true)]
        [string]
        $ResourceGroupName,
        [Parameter (Mandatory=$true)]
        [string]
        $ObjectId        
    )
    if($objectId -ne $null)
    {
        $roleAssignment = (Get-AzRoleAssignment `
            -ObjectId $ObjectId `
            -ResourceGroupName $ResourceGroupName `
            -RoleDefinitionName $RoleDefinitionName)
        
        if($roleAssignment)
        {
            Write-Host "$ResourceGroupName's $RoleDefinitionName Role Assignment already exists."
        }
        else
        {         
            Write-Host "Assigning $RoleDefinitionName access to ($ResourceGroupName)..."
            New-AzRoleAssignment `
                -ObjectId $ObjectId `
                -ResourceGroupName $ResourceGroupName `
                -RoleDefinitionName $RoleDefinitionName
            Write-Host "$RoleDefinitionName access assigned to ($ResourceGroupName)."
        }        
    }
}

function Set-ServicePrincipal {
    [CmdletBinding()]
	param(
		[Parameter(Mandatory=$True)]
        [string] $servicePrincipalName,
        [Parameter(Mandatory=$True)]
        [string] $solutionAbbreviation,
		[Parameter(Mandatory=$True)]
		[string] $environmentAbbreviation
    )
    
    $scriptsDirectory = Split-Path $PSScriptRoot -Parent
    
    . ($scriptsDirectory + '\Scripts\Install-AzModuleIfNeeded.ps1')
    Install-AzModuleIfNeeded | Out-Null

    . ($scriptsDirectory + '\Scripts\Add-AzAccountIfNeeded.ps1')
    Add-AzAccountIfNeeded
        
    Write-Host "`nCurrent subscription:`n"
    $currentSubscription = (Get-AzContext).Subscription
    Write-Host "$($currentSubscription.Name) -  $($currentSubscription.Id)"

    Write-Host "`nAvailable subscriptions:"
    Write-Host (Get-AzSubscription | Select-Object -Property Name, Id)
    Write-Host "`n"

    $subscriptionId = Read-Host -Prompt "If you would like to use other subscription than '$($currentSubscription.Name)' `nprovide the subscription id, otherwise press enter to continue."
    if ($subscriptionId)
    {        
        Set-AzContext -SubscriptionId $subscriptionId
        $currentSubscription = (Get-AzContext).Subscription
        Write-Host "Selected subscription: $($currentSubscription.Name) -  $($currentSubscription.Id)"
    }
                        
    Write-Host "The service principals name is $servicePrincipalName"

    $scope = "/subscriptions/$($subscription.Id)"

    #region Remove service principal if it exists
    $servicePrincipal = Get-AzADServicePrincipal -DisplayName $servicePrincipalName
    if($null -ne $servicePrincipal)
    {
        Write-Host "The service principal already exists.  Removing..."
        Remove-AzADServicePrincipal -ObjectId $servicePrincipal.Id -Force
        Remove-AzADApplication -ApplicationId $servicePrincipal.ApplicationId -Force
        Write-Host "The service principal has been removed."
    }
    #endregion

    #region Create service principal
    Write-Host "The service principal is being created..."
    $servicePrincipal = New-AzADServicePrincipal -Role "Owner" -Scope $scope -DisplayName $servicePrincipalName -SkipAssignment
    Write-Host "The service principal has been created."
    #endregion

    #region Assing role to resource groups
    Write-Host "Assigning service principal to resouce groups..."
    Start-Sleep -Seconds 10
    $resourceGroupTypes = "compute", "data", "prereqs"
    foreach ($resourceGroupType in $resourceGroupTypes)
    {
        $resourceGroupName = "$solutionAbbreviation-$resourceGroupType-$environmentAbbreviation"        
        Set-Role -RoleDefinitionName "Contributor" -ResourceGroupName $resourceGroupName -ObjectId $servicePrincipal.Id | Out-Null
    }

    return $currentSubscription.Id
}