$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Installs a module if it is needed.

.DESCRIPTION
Installs a module if it is needed.

.EXAMPLE
Install-AzKeyVaultModuleIfNeeded

#>
function Install-VSTeamModuleIfNeeded {
    [CmdletBinding()]
    param(
    )
        $scriptsDirectory = Split-Path $PSScriptRoot -Parent
    
        . ($scriptsDirectory + '\Scripts\Install-ModuleIfNeeded.ps1')
        Install-ModuleIfNeeded -Name VSTeam -Version "6.1.3" -Verbose
}