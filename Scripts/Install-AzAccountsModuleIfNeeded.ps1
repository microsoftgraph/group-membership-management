$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Installs a module if it is needed.

.DESCRIPTION
Installs a module if it is needed.

.EXAMPLE
Install-AzAccountsModuleIfNeeded

#>
function Install-AzAccountsModuleIfNeeded {
    [CmdletBinding()]
    param(
    )
        $scriptsDirectory = Split-Path $PSScriptRoot -Parent
    
        . ($scriptsDirectory + '\Scripts\Install-ModuleIfNeeded.ps1')
        Install-ModuleIfNeeded -Name Az.Accounts -Version "1.8.1" -Verbose
}