$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
This script adds secrets to a key vault

.PARAMETER keyVaultName
Name for the new service principal

.PARAMETER secrets
Array with secrets to store in the  key vault ie ("name1=value1", "name2=value2")

.EXAMPLE
Set-KeyVaultSecrets  -keyVaultName "<key value name>" `
                    -secrets ("name1=value1", "name2=value2")
#>

function Set-KeyVaultSecrets {
    [CmdletBinding()]
	param(
        [Parameter(Mandatory=$True)]
        [string] $keyVaultName,
        [Parameter(Mandatory=$True)]
        [System.Collections.Generic.List[System.Object]] $keyValuePairs
    )

    $scriptsDirectory = Split-Path $PSScriptRoot -Parent

    . ($scriptsDirectory + '\Scripts\Add-AzAccountIfNeeded.ps1')
    Add-AzAccountIfNeeded

    . ($scriptsDirectory + '\Scripts\Install-AzModuleIfNeeded.ps1')
	Install-AzModuleIfNeeded

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

    Write-Host "`nSaving secrets..."

    $keyVault = Get-AzKeyVault -VaultName $keyVaultName
    foreach($kvPair in $keyValuePairs)
    {
        $secretName = $kvPair.Key
        $secretValue = $kvPair.SecretValue

        Write-Host "`nSaving secret: $secretName"

        Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
						 -Name $secretName `
						 -SecretValue $secretValue
    }

    Write-Host "`nSaving secrets complete."
}