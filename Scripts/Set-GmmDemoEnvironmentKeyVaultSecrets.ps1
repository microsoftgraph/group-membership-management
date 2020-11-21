$ErrorActionPreference = "Stop"
<# 
.SYNOPSIS
This script adds secrets to a key vault

.PARAMETER keyVaultName
Name for the new service principal

.PARAMETER secrets
Array with secrets to store in the  key vault ie ("name1=value1", "name2=value2")

.EXAMPLE
Set-GmmDemoEnvironmentKeyVaultSecrets   -solutionAbbreviation "<solution>" `
                                        -environmentAbbreviation "<env>" `
                                        -tenantName "<tenantName>" `
                                        -tenantAdminPassword "<tenantAdminPassword>" `
                                        -tenantAdminUsername  "<tenantAdminUsername>"  

solutionAbbreviation is an optional parameter
tenantAdminUsername is an optional parameter
#>

function Set-GmmDemoEnvironmentKeyVaultSecrets {
    [CmdletBinding()]
	param(
        [Parameter(Mandatory=$False)]
        [string] $solutionAbbreviation,
        [Parameter(Mandatory=$True)]
        [string] $environmentAbbreviation,
        [Parameter(Mandatory=$True)]
        [string] $tenantName,
        [Parameter(Mandatory=$True)]
        [string] $tenantAdminPassword,
        [Parameter(Mandatory=$False)]
        [string] $tenantAdminUsername        
    )
    
    $scriptsDirectory = Split-Path $PSScriptRoot -Parent
    
    . ($scriptsDirectory + '\Scripts\Set-KeyVaultSecrets.ps1')
    
    if (!($solutionAbbreviation))
    {
        $solutionAbbreviation = "gmm"
    }

    if(!($tenantAdminUsername))
    {
        $tenantAdminUsername = "admin@$tenantName.onmicrosoft.com"
    }

    $keyVaultName = "$solutionAbbreviation-prereqs-$environmentAbbreviation"
    
    Set-KeyVaultSecrets -keyVaultName $keyVaultName `
                        -secrets ("tenantName=$tenantName", "tenantAdminUsername=$tenantAdminUsername", "tenantAdminPassword=$tenantAdminPassword")
}