$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Stores the notifier's actionable message provider id in prereqs keyvault.

.PARAMETER SubscriptionName
Subscription Name

.PARAMETER SolutionAbbreviation
Solution Abbreviation

.PARAMETER EnvironmentAbbreviation
Environment Abbreviation

.PARAMETER ProviderId
Provider Id

.EXAMPLE
Set-NotifierProviderId	-SubscriptionName "<subscription name>" `
						-SolutionAbbreviation "gmm" `
						-EnvironmentAbbreviation "<env>" `
						-ProviderId "<provider id>" `
						-Verbose
#>
function Set-NotifierProviderId {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory=$True)]
		[string] $SubscriptionName,
		[Parameter(Mandatory=$True)]
		[string] $SolutionAbbreviation,
		[Parameter(Mandatory=$True)]
		[string] $EnvironmentAbbreviation,
        [Parameter(Mandatory=$True)]
		[string] $ProviderId,
		[Parameter(Mandatory=$False)]
		[string] $ErrorActionPreference = $Stop
	)
	Write-Verbose "Set-NotifierProviderId starting..."

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

	#region Store Provider Id in KeyVault
    Write-Verbose "Provider Id is $ProviderId"

    $notifierProviderIdKeyVaultSecretName = "notifierProviderId"
	$notifierProviderIdSecret = ConvertTo-SecureString -AsPlainText -Force  $ProviderId
	Set-AzKeyVaultSecret -VaultName $keyVault.VaultName `
						 -Name $notifierProviderIdKeyVaultSecretName `
						 -SecretValue $notifierProviderIdSecret
	Write-Verbose "$notifierProviderIdKeyVaultSecretName added to vault..."

	#endregion
	Write-Verbose "Set-NotifierProviderId completed."
}