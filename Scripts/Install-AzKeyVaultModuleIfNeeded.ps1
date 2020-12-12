$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Installs a module if it is needed.

.DESCRIPTION
Installs a module if it is needed.

.EXAMPLE
Install-AzKeyVaultModuleIfNeeded

#>
function Install-AzKeyVaultModuleIfNeeded {
    [CmdletBinding()]
    param(
    )
        $scriptsDirectory = Split-Path $PSScriptRoot -Parent
                    
        . ($scriptsDirectory + '\Scripts\Install-ModuleIfNeeded.ps1')
        Install-ModuleIfNeeded -Name Az.KeyVault -Version "2.0.0" -Verbose
}