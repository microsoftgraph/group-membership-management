$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Installs a module if it is needed.

.DESCRIPTION
Installs a module if it is needed.

.EXAMPLE
Install-MSGraphIfNeeded

#>
function Install-MSGraphIfNeeded {
    [CmdletBinding()]
    param(
    [Parameter(Mandatory = $False)]
    [string] $BaseScriptDirectory
)

$scriptsDirectory = (Split-Path $PSScriptRoot -Parent) + "\Scripts"

if ($BaseScriptDirectory) {
    $scriptsDirectory = $BaseScriptDirectory
}

. ($scriptsDirectory + '\Install-ModuleIfNeeded.ps1')
Install-ModuleIfNeeded -Name Microsoft.Graph -Version "2.17.0" -Verbose
}