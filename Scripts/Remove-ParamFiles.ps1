$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
This script removed the given parameter files from the required directories.

.PARAMETER TargetEnvironmentAbbreviation
The Environment Abbreviation of the environment to be deleted.

.PARAMETER RepoPath
The path to your cloned private repository.

.EXAMPLE
Remove-ParamFiles.ps1   -TargetEnvironmentAbbreviation "<TargetEnvironmentAbbreviation>" `
                        -RepoPath "C:\Users\username\ReposTest\Private-GMM"
#>
function Remove-ParamFiles.ps1 {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
		[string] $TargetEnvironmentAbbreviation,
        [Parameter(Mandatory)]
		[string] $RepoPath
    )

    $fileSuffix = ".$TargetEnvironmentAbbreviation.json"

    $paths = @(
        @{ Path = 'Infrastructure\data'; FilePrefix = 'parameters' },
        @{ Path = 'Service\GroupMembershipManagement\Hosts\*\Infrastructure\data'; FilePrefix = 'parameters' },
        @{ Path = 'Service\GroupMembershipManagement\Hosts\*\Infrastructure\compute'; FilePrefix = 'parameters' },
        @{ Path = 'UI\web-app'; FilePrefix = 'appsettings' }
    )

    # Check that the RepoPath is valid
    If ((-Not (Test-Path $RepoPath)) -or (-Not (Test-Path "$RepoPath\$($paths[0].Path)")))
    {
        Throw "The provided path to your repository $RepoPath does not exist or is incorrect. Please verify and try again!"
    }

    # Delete files
    foreach ($path in $paths) {

        $fullPath = "$RepoPath\$($path.Path)"
        $files = Get-ChildItem -Path $fullPath -Recurse -Include '*parameters' | Get-ChildItem -Filter "$($path.FilePrefix)$fileSuffix"

        foreach ($file in $files) {
            Write-Host $file.FullName
            Remove-Item $file.FullName
        }
    }
}