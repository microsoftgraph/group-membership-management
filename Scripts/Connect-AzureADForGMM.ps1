$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Short description

.DESCRIPTION
Long description

.PARAMETER SolutionAbbreviation
Parameter description

.PARAMETER EnvironmentAbbreviation
Parameter description

.PARAMETER Version
Parameter description

.EXAMPLE
An example
#>
function Connect-AzureADForGMM {
	[CmdletBinding()]
	param(
        [Parameter(
            ParameterSetName='WebPartTenant',
            Mandatory=$True
        )]
        [string] $SolutionAbbreviation,
        
        [Parameter(
            ParameterSetName='WebPartTenant',
            Mandatory=$True
        )]
        [string] $EnvironmentAbbreviation,

        [Parameter(
            ParameterSetName='ServiceTenant',
            Mandatory=$True
        )]
        [ValidateSet(
            $true
        )]
		[switch] $ServiceTenant
    )
    #Requires -Version 5
    $scriptsDirectory = Split-Path $PSScriptRoot -Parent

    . ($scriptsDirectory + '\Scripts\Install-AzureADModuleIfNeeded.ps1')
    Install-AzureADModuleIfNeeded

    $microsoftTenantId = "your azure ad tenant id"

    $keyVaultName = "$SolutionAbbreviation-webpart-$EnvironmentAbbreviation"
    try {
        $tenantDetail = Get-AzureADTenantDetail
        if($PSCmdlet.ParameterSetName -eq "ServiceTenant") {
            $tenantNames = $tenantDetail.VerifiedDomains.Name
            $expectedTenantName = "microsoft.onmicrosoft.com"
            if($tenantNames -contains $expectedTenantName) {
                Write-Verbose "Azure AD previously connected."
            }
            else {
                throw "Previously connected to a different tenant..."
            }
        }
        elseif ($PSCmdlet.ParameterSetName -eq "WebPartTenant")  {
            . ($scriptsDirectory + '\Scripts\Install-AzKeyVaultModuleIfNeeded.ps1')
            Install-AzKeyVaultModuleIfNeeded

            . ($scriptsDirectory + '\Scripts\Add-AzAccountIfNeeded.ps1')
            Add-AzAccountIfNeeded

            $secureSharePointTenantName = Get-AzKeyVaultSecret -VaultName $keyVaultName `
                                                -Name "sharePointTenantName"
            $tenantName = $secureSharePointTenantName.SecretValueText
            $tenantNames = $tenantDetail.VerifiedDomains.Name
            $expectedTenantName = "$tenantName.onmicrosoft.com"
            if($tenantNames -contains $expectedTenantName) {
                Write-Verbose "Azure AD previously connected."
            }
            else {
                throw "Previously connected to a different tenant..."
            }  
        }
    }
    catch
    {
        Write-Verbose "Azure AD not previously connected.  Connecting now..."
        if($PSCmdlet.ParameterSetName -eq "ServiceTenant") {
            Connect-AzureAD -TenantId $microsoftTenantId
        }
        elseif ($PSCmdlet.ParameterSetName -eq "WebPartTenant")  {

        
            $secureUsername = Get-AzKeyVaultSecret -VaultName $keyVaultName `
                                                            -Name "sharePointTenantAdminUsername"
            $securePassword = Get-AzKeyVaultSecret -VaultName $keyVaultName `
                                                            -Name "sharePointTenantAdminPassword"

            $credential = New-Object System.Management.Automation.PSCredential($secureUsername.SecretValueText, $securePassword.SecretValue)
            
            Connect-AzureAD -Credential $credential
        }
        Write-Verbose "Azure AD connected."
    }
}