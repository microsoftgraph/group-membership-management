$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Installs a module if it is needed.

.DESCRIPTION
Installs a module if it is needed.

.EXAMPLE
Install-AzTableModuleIfNeeded

#>
function Install-AzTableModuleIfNeeded {
    [CmdletBinding()]
    param(
    )
        . ($PSScriptRoot + '\Install-ModuleIfNeeded.ps1')
        Install-ModuleIfNeeded -Name AzTable -Version "2.1.0" -Verbose
}