$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
This script adds the teams channel service account related secrets to the prereqs key vault

.PARAMETER teamsChannelServiceAccountUsername
Username of the teams channel service account

.PARAMETER teamsChannelServiceAccountPassword
Password of the teams channel service account

.PARAMETER teamsChannelServiceAccountObjectId
Object Id of the teams channel service account

.EXAMPLE
$teamsChannelServiceAccountUsername = ConvertTo-SecureString -AsPlainText -Force "<Service Account Username>"
$teamsChannelServiceAccountPassword = ConvertTo-SecureString -AsPlainText -Force "<Service Account Password>"
$teamsChannelServiceAccountObjectId = ConvertTo-SecureString -AsPlainText -Force "<Service Account Object Id"

Set-TeamsChannelServiceAccountSecrets   -SubscriptionName "<Subscription Name>" `
                                        -SolutionAbbreviation "<Solution Abbreviation>" `
                                        -EnvironmentAbbreviation "<Environment Abbreviation>" `
                                        -teamsChannelServiceAccountUsername $teamsChannelServiceAccountUsername `
                                        -teamsChannelServiceAccountPassword $teamsChannelServiceAccountPassword `
                                        -teamsChannelServiceAccountObjectId $teamsChannelServiceAccountObjectId
#>

function Set-TeamsChannelServiceAccountSecrets {
    [CmdletBinding()]
	param(
        [Parameter(Mandatory=$True)]
		[string] $SubscriptionName,
		[Parameter(Mandatory=$True)]
		[string] $SolutionAbbreviation,
		[Parameter(Mandatory=$True)]
		[string] $EnvironmentAbbreviation,
        [Parameter(Mandatory=$True)]
        [SecureString] $teamsChannelServiceAccountUsername,
        [Parameter(Mandatory=$True)]
        [SecureString] $teamsChannelServiceAccountPassword,
        [Parameter(Mandatory=$True)]
        [SecureString] $teamsChannelServiceAccountObjectId
    )

    Write-Verbose "Set-TeamsChannelServiceAccountSecrets starting..."

	$scriptsDirectory = Split-Path $PSScriptRoot -Parent

	. ($scriptsDirectory + '\Scripts\Add-AzAccountIfNeeded.ps1')
	Add-AzAccountIfNeeded

	Set-AzContext -SubscriptionName $SubscriptionName

	. ($scriptsDirectory + '\Scripts\Install-AzModuleIfNeeded.ps1')
	Install-AzModuleIfNeeded

	$keyVaultName = "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation"
    $keyVault = Get-AzKeyVault -VaultName $keyVaultName

    if($null -eq $keyVault)
	{
		throw "The KeyVault ($keyVaultName) does not exist. Unable to continue."
	}

    Write-Verbose "Adding teamsChannelServiceAccountUsername to vault..."
	Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
						 -Name "teamsChannelServiceAccountUsername" `
						 -SecretValue $teamsChannelServiceAccountUsername
	Write-Verbose "teamsChannelServiceAccountUsername added to vault..."

    Write-Verbose "Adding teamsChannelServiceAccountPassword to vault..."
	Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
						 -Name "teamsChannelServiceAccountPassword" `
						 -SecretValue $teamsChannelServiceAccountPassword
	Write-Verbose "teamsChannelServiceAccountPassword added to vault..."

    Write-Verbose "Adding teamsChannelServiceAccountObjectId to vault..."
	Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
						 -Name "teamsChannelServiceAccountObjectId" `
						 -SecretValue $teamsChannelServiceAccountObjectId
	Write-Verbose "teamsChannelServiceAccountObjectId added to vault..."

}