$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Script to tear down an existing environment.

.PARAMETER solutionAbbreviation
Abbreviation used to denote the overall solution (or application). Length 1-3.

.PARAMETER environmentAbbreviation
Abbreviation for the environment. Length 1-3.

.PARAMETER resourceGroupLocation
Azure location where the resource groups and its resources to be deleted are.


.EXAMPLE
Delete-Environment -solutionAbbreviation "<solutionAbbreviation>" `
                -environmentAbbreviation "<environmentAbbreviation>" `
                -resourceGroupLocation "<resourceGroupLocation>" `

#>

function Set-Subscription {

    Connect-AzAccount

    if (-not $SubscriptionId) {
		Write-Host "`nCurrent subscription:`n"
		$currentSubscription = (Get-AzContext).Subscription
		Write-Host "$($currentSubscription.Name) -  $($currentSubscription.Id)"

		Write-Host "`nAvailable subscriptions:"
		Write-Host (Get-AzSubscription | Select-Object -Property Name, Id)
		Write-Host "`n"
		$SubscriptionId = Read-Host -Prompt "If you would like to use other subscription than '$($currentSubscription.Name)' `nprovide the subscription id, otherwise press enter to continue."
	}

    if ($SubscriptionId)
    {
        Set-AzContext -SubscriptionId $SubscriptionId
        $currentSubscription = (Get-AzContext).Subscription
        Write-Host "Selected subscription: $($currentSubscription.Name) -  $($currentSubscription.Id)"
    }

    return $SubscriptionId;
}

function Set-Environment {
    Param
    (
        [Parameter (
            Mandatory=$true,
            HelpMessage="The abbreviation for your solution."
        )]
        [string]
        $solutionAbbreviation,
        [Parameter (
            Mandatory=$true,
            HelpMessage="The abbreviation for your environment."
        )]
        [string]
        $environmentAbbreviation,
        [Parameter (
            Mandatory=$false,
            HelpMessage="The Azure location where the resource groups are."
        )]
        [string]
        $resourceGroupLocation
    )

    $scriptsDirectory = Split-Path $PSScriptRoot -Parent

    . ($scriptsDirectory + '\Scripts\Add-AzAccountIfNeeded.ps1')
    Add-AzAccountIfNeeded | Out-Null

    Set-Subscription

    #region Generate Resource Group Names
    $resourceGroupTypes = "compute", "data", "prereqs"
    $resourceGroupNames = @()
    foreach ($resourceGroupType in $resourceGroupTypes)
    {
        $resourceGroupNames += "$solutionAbbreviation-$resourceGroupType-$environmentAbbreviation"
    }
    #endregion

    #region Delete resource groups
    foreach ($resourceGroupName in $resourceGroupNames)
    {
        $resourceGroup = Get-AzResourceGroup `
            -Name $resourceGroupName `
            -ErrorAction SilentlyContinue `
            -ErrorVariable ev
        if(-not $ev -and $resourceGroup)
        {
            Write-Host "Removing the existing resource group $resourceGroupName..."
            Remove-AzResourceGroup `
                -Name $resourceGroup.ResourceGroupName `
                -Force
            Write-Host "Removed the existing resource group $resourceGroupName."
        }
        else 
        {
            Write-Host "Error. Could not find $resourceGroupName."
        }
    }
    #endregion

    #region Delete PreReqs KeyVault in soft-delete mode.
    $resourceGroupName = "$solutionAbbreviation-prereqs-$environmentAbbreviation"
    Write-Host "Deleting prereqs keyvault in soft-delete mode for $resourceGroupName..."
    $prereqsKeyVault = Remove-AzKeyVault `
                            -VaultName $prereqsKeyVault.VaultName`
                            -Location $resourceGroupLocation `
                            -InRemovedState

    Write-Host "Prereqs keyvault $($prereqsKeyVault.VaultName) deleted."
    #endregion

    #region Delete Data KeyVault in soft-delete mode.
    $resourceGroupName = "$solutionAbbreviation-data-$environmentAbbreviation"
    Write-Host "Deleting data keyvault in soft-delete mode for $resourceGroupName..."
    $dataKeyVault = Remove-AzKeyVault `
                            -VaultName $dataKeyVault.VaultName`
                            -Location $resourceGroupLocation `
                            -InRemovedState

    Write-Host "Data keyvault $($dataKeyVault.VaultName) deleted."
    #endregion

    Write-Host "Delete-Environment complete."
}