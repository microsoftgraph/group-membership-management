$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Installs a module if it is needed.

.DESCRIPTION
Installs a module if it is needed.

.EXAMPLE
Install-AzureADModuleIfNeeded

#>
function Install-AzureADModuleIfNeeded {
    [CmdletBinding()]
    param(
    )
        $scriptsDirectory = Split-Path $PSScriptRoot -Parent
            
        . ($scriptsDirectory + '\Scripts\Install-ModuleIfNeeded.ps1')
        Install-ModuleIfNeeded -Name AzureAD -Version "2.0.2.76" -Verbose
}