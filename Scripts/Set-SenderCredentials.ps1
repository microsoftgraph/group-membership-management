$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Stores the user name and password of the user (mail sender) in prereqs keyvault

.PARAMETER SubscriptionName
Subscription Name

.PARAMETER SolutionAbbreviation
Solution Abbreviation

.PARAMETER EnvironmentAbbreviation
Environment Abbreviation

.PARAMETER SenderUsername
Sender Username

.PARAMETER SenderPassword
Sender Password

.EXAMPLE
Set-SenderCredentials	-SubscriptionName "<subscription name>" `
                        -SolutionAbbreviation "gmm" `
                        -EnvironmentAbbreviation "<env>" `
                        -SenderUsername "<sender username>" `
                        -SenderPassword "<sender password>" `
                        -Verbose
#>
function Set-SenderCredentials {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory=$True)]
		[string] $SubscriptionName,
		[Parameter(Mandatory=$True)]
		[string] $SolutionAbbreviation,
		[Parameter(Mandatory=$True)]
		[string] $EnvironmentAbbreviation,
        [Parameter(Mandatory=$True)]
		[string] $SenderUsername,
		[Parameter(Mandatory=$True)]
		[string] $SenderPassword,
		[Parameter(Mandatory=$False)]
		[string] $ErrorActionPreference = $Stop
	)
	Write-Verbose "Set-SenderCredentials starting..."

	$scriptsDirectory = Split-Path $PSScriptRoot -Parent
		
	Set-AzContext -SubscriptionName $SubscriptionName

	Connect-AzureAD

	. ($scriptsDirectory + '\Scripts\Install-AzKeyVaultModuleIfNeeded.ps1')
	Install-AzKeyVaultModuleIfNeeded

	. ($scriptsDirectory + '\Scripts\Add-AzAccountIfNeeded.ps1')
	Add-AzAccountIfNeeded
	
	$keyVaultName = "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation"
    $keyVault = Get-AzKeyVault -VaultName $keyVaultName

    if($null -eq $keyVault)
	{
		throw "The KeyVault ($keyVaultName) does not exist. Unable to continue."
	}

	#region Store Sender Username in KeyVault
    Write-Verbose "Sender Username is $SenderUserName"
	
    $senderUsernameKeyVaultSecretName = "senderUsername"
	$senderUsernameSecret = ConvertTo-SecureString -AsPlainText -Force  $SenderUserName
	Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
						 -Name $senderUsernameKeyVaultSecretName `
						 -SecretValue $senderUsernameSecret
	Write-Verbose "$senderUsernameKeyVaultSecretName added to vault..."

	#region Store Sender Password secret in KeyVault
	$senderPasswordKeyVaultSecretName = "senderPassword"
	$senderPasswordSecret = ConvertTo-SecureString -AsPlainText -Force  $SenderPassword
	Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
						 -Name $senderPasswordKeyVaultSecretName `
						 -SecretValue $senderPasswordSecret
	Write-Verbose "$senderPasswordKeyVaultSecretName added to vault..."

	#endregion
	Write-Verbose "Set-SenderCredentials completed."
}