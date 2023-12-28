$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
This script adds secrets to a key vault

.PARAMETER keyVaultName
Name for the new service principal

.PARAMETER secrets
Array with secrets to store in the  key vault ie ("name1=value1", "name2=value2")

.EXAMPLE

$secureTenantName = ConvertTo-SecureString -AsPlainText -Force "<tenantName>"
$secureTenantAdminUsername = ConvertTo-SecureString -AsPlainText -Force "<tenantAdminPassword>"
$secureTenantAdminPassword = ConvertTo-SecureString -AsPlainText -Force "<tenantAdminUsername>"

Set-GmmDemoEnvironmentKeyVaultSecrets   -solutionAbbreviation "<solution>" `
                                        -environmentAbbreviation "<env>" `
                                        -secureTenantName $secureTenantName `
                                        -secureTenantAdminUsername  $secureTenantAdminUsername `
                                        -secureTenantAdminPassword "$secureTenantAdminPassword

solutionAbbreviation is an optional parameter
#>

function Set-GmmDemoEnvironmentKeyVaultSecrets {
    [CmdletBinding()]
	param(
        [Parameter(Mandatory=$False)]
        [string] $solutionAbbreviation,
        [Parameter(Mandatory=$True)]
        [string] $environmentAbbreviation,
        [Parameter(Mandatory=$True)]
        [SecureString] $secureTenantName,
        [Parameter(Mandatory=$True)]
        [SecureString] $secureTenantAdminUsername,
        [Parameter(Mandatory=$True)]
        [SecureString] $secureTenantAdminPassword
    )

    $scriptsDirectory = Split-Path $PSScriptRoot -Parent

    . ($scriptsDirectory + '\Scripts\Set-KeyVaultSecrets.ps1')

    if (!($solutionAbbreviation))
    {
        $solutionAbbreviation = "gmm"
    }

    $keyVaultName = "$solutionAbbreviation-prereqs-$environmentAbbreviation"

    $keyValuePairs = New-Object 'System.Collections.Generic.List[System.Object]'

    $keyValuePairs.Add([PSCustomObject]@{Key="tenantName";SecretValue=$secureTenantName})
    $keyValuePairs.Add([PSCustomObject]@{Key="tenantAdminUsername";SecretValue=$secureTenantAdminUsername})
    $keyValuePairs.Add([PSCustomObject]@{Key="tenantAdminPassword";SecretValue=$secureTenantAdminPassword})

    Set-KeyVaultSecrets -keyVaultName $keyVaultName `
                        -keyValuePairs $keyValuePairs
}