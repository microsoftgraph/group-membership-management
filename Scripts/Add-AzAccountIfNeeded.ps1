$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Signs user in if needed.

.DESCRIPTION
Signs user in if needed.

.EXAMPLE
Add-AzAccountIfNeeded
#>
function Add-AzAccountIfNeeded {
    [CmdletBinding()]
    param(
    )
    #Requires -Version 5
    $scriptsDirectory = Split-Path $PSScriptRoot -Parent
    
    . ($scriptsDirectory + '\Scripts\Install-AzModuleIfNeeded.ps1')
    Install-AzModuleIfNeeded | Out-Null
    
    $context = Get-AzContext
    $account = $context.Account

    if($null -eq $account) {
        Write-Verbose "AzAccount does not already exist.  Adding AzAccount now..."
        Add-AzAccount
        Write-Verbose "AzAccount added."
    } else {
        Write-Verbose "AzAccount previously added."
    }
}