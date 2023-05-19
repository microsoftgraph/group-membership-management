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

	VerifyKeyVaultSecrets

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