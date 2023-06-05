<#
.SYNOPSIS
Post Deployment Script
This script can be used to run custom steps.

.PARAMETER SolutionAbbreviation
Solution Abbreviation

.PARAMETER EnvironmentAbbreviation
Environment Abbreviation

.EXAMPLE
PostDeploymentScript	-SolutionAbbreviation "<solution>" `
						-EnvironmentAbbreviation "<environment>" `
						-Verbose
#>
function PostDeploymentScript {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory = $True)]
		[string] $SolutionAbbreviation,
		[Parameter(Mandatory = $True)]
		[string] $EnvironmentAbbreviation
	)
	Write-Host "PostDeploymentScript starting..."

	$verbose = ($true -eq $PSBoundParameters.Verbose)

	. ($PSScriptRoot + '\Confirm-KeyVaultSecrets.ps1')
	Confirm-KeyVaultSecrets -SolutionAbbreviation $SolutionAbbreviation -EnvironmentAbbreviation $EnvironmentAbbreviation -Verbose:$verbose

	. ($PSScriptRoot + '\Copy-SyncJobsToSQL.ps1')
	Copy-SyncJobsToSQL -SolutionAbbreviation $SolutionAbbreviation -EnvironmentAbbreviation $EnvironmentAbbreviation -Verbose:$verbose

	Write-Host "PostDeploymentScript completed."
}

function VerifyKeyVaultSecrets {

	$prereqsKeyVault = "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation"
	$graphAppCertificateName = Get-AzKeyVaultSecret -VaultName $prereqsKeyVault -Name "graphAppCertificateName"
    if(!$graphAppCertificateName){
		$secret = ConvertTo-SecureString -String 'not-set' -AsPlainText -Force
		Set-AzKeyVaultSecret -VaultName $prereqsKeyVault -Name "graphAppCertificateName" -SecretValue $secret
	}
}