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
Set-ServicePrincipal -SolutionAbbreviation "<solutionAbbreviation>" `
                     -EnvironmentAbbreviation "<environmentAbbreviation>" `
                     -Clean $False
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
        [string] $solutionAbbreviation,
		[Parameter(Mandatory=$True)]
		[string] $environmentAbbreviation,
        [Parameter (Mandatory=$False)]
        [bool] $Clean = $False
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

    Write-Host "Please ensure you have ownership permissions on this subscription."

    $scope = "/subscriptions/$($subscription.Id)"

    $servicePrincipalName = "$solutionAbbreviation-serviceconnection-$environmentAbbreviation";

    Write-Host "The service principals name is $servicePrincipalName"

    #region Remove service principal if it exists
    $servicePrincipal = Get-AzADServicePrincipal -DisplayName $servicePrincipalName
    if($Clean -and ($null -ne $servicePrincipal))
    {
        Write-Host "The service principal already exists.  Removing..."
        Remove-AzADServicePrincipal -ObjectId $servicePrincipal.Id -Force
        Remove-AzADApplication -ApplicationId $servicePrincipal.ApplicationId -Force
        $servicePrincipal = $null
        Write-Host "The service principal has been removed."
    }
    #endregion

    if($null -eq $servicePrincipal) {
        #region Create service principal
        Write-Host "The service principal is being created..."
        $servicePrincipal = New-AzADServicePrincipal -Role "Owner" -Scope $scope -DisplayName $servicePrincipalName
        Write-Host "The service principal has been created."
        #endregion

        #region Assign role to resource groups
        Write-Host "Waiting for service principal to propagate..."
        # basically,
        Start-Sleep -Seconds 30
    }

    Write-Host "The service principal has propagated and is ready for use."
}