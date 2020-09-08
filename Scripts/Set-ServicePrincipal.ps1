$ErrorActionPreference = "Stop"
<# 
.SYNOPSIS
This script creates a service principal

.PARAMETER ServicePrincipalName
Name for the new service principal

.EXAMPLE
Set-ServicePrincipal -ServicePrincipalName "<name>"
#>

function Set-ServicePrincipal {
    [CmdletBinding()]
	param(
		[Parameter(Mandatory=$True)]
        [string] $ServicePrincipalName
    )
    
    $scriptsDirectory = Split-Path $PSScriptRoot -Parent
    
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
                        
    Write-Host "The service principals name is $ServicePrincipalName"

    $scope = "/subscriptions/$($subscription.Id)"

    #region Remove service principal if it exists
    $servicePrincipal = Get-AzADServicePrincipal -DisplayName $ServicePrincipalName
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
    $servicePrincipal = New-AzADServicePrincipal -Role "Owner" -Scope $scope -DisplayName $ServicePrincipalName -SkipAssignment
    Write-Host "The service principal has been created."
    #endregion
        
    return $currentSubscription.Id
}