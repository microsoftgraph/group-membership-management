$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Installs a module if it is needed.

.DESCRIPTION
Installs a module if it is needed.

.PARAMETER Name
Name of Module to install.

.PARAMETER Version
Version of Module to install.

.EXAMPLE
Install-ModuleIfNeeded -Name Az.Accounts -Version "1.0.0"
#>
function Install-ModuleIfNeeded {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$True)]
        [string] $Name,
        [Parameter(Mandatory=$True)]
        [string] $Version
    )
    if($null -ne $env:BUILD_BUILDNUMBER -and $Name -like "Az.*")
    {
        Write-Verbose "Will not install Module: $Name, Version: $Version while running in Azure DevOps."
        return
    }
    $moduleVersions = Get-Module -ListAvailable -Name $Name
    if ($moduleVersions | Where {$_.Version -eq $Version}) {
        Write-Verbose "$Name Module Version: $Version is installed.  Skipping installation."
    } else {
        Write-Verbose "$Name Module Version: $Version is not installed.  Installing..."
        Install-Module $Name -RequiredVersion $Version -Scope CurrentUser -Force -AllowClobber
        # You may need to add '-SkipPublisherCheck' to the previous line if there has been
        # an update to the PowerShell module's certificate. Make sure you trust the package before doing this.
        Write-Verbose "$Name Module Version: $Version installation complete."
    }
    Write-Verbose "Importing module: $Name, Version: $Version..."
    Import-Module $Name -RequiredVersion $Version -Force
    Write-Verbose "$Name Module Version: $Version import complete."
}