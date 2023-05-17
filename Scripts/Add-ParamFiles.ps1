$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Generate environment parameter files by copying and renaming them from another engineer.

.PARAMETER EnvironmentAbbreviation
Your Environment Abbreviation

.PARAMETER SourceEnvironmentAbbreviation
The Environment Abbreviation of another engineer that has their environment set up 

.PARAMETER RepoPath
The path to your cloned private repository

.EXAMPLE
Add-ParamFiles  -EnvironmentAbbreviation "<EnvironmentAbbreviation>" `
                -SourceEnvironmentAbbreviation "<SourceEnvironmentAbbreviation>" `
                -RepoPath "C:\Users\username\ReposTest\Private-GMM" 
#>
function Add-ParamFiles {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
		[string] $EnvironmentAbbreviation,
        [Parameter(Mandatory)]
		[string] $SourceEnvironmentAbbreviation,
        [Parameter(Mandatory)]
		[string] $RepoPath
    )

    $sourceFileSuffix = ".$SourceEnvironmentAbbreviation.json"
    $destinationFileSuffix = ".$EnvironmentAbbreviation.json"

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

    # Check that the source file is present in all the parameter directories
    foreach ($path in $paths) {

        $fullPath = "$RepoPath\$($path.Path)"
        $paramDirectories = Get-ChildItem -Path $fullPath -Recurse -Include '*parameters'

        foreach ($paramDirectory in $paramDirectories) {

            $pathToSource = "$($paramDirectory.FullName)\$($path.FilePrefix)$sourceFileSuffix"

            If (-Not (Test-Path $pathToSource))
            {
                Throw "Source file $sourceFileName not present in $pathToSource! The SourceEnvironmentAbbreviation does not have parameter files in all the folders it should have. Check your SourceEnvironmentAbbreviation or try a different one."
            }
        }
     }

    Write-Host "Files Added:\n"
    # Duplicate and rename files
    foreach ($path in $paths) {

        $fullPath = "$RepoPath\$($path.Path)"
        $paramDirectories = Get-ChildItem -Path $fullPath -Recurse -Include '*parameters'

        foreach ($paramDirectory in $paramDirectories) {
            Write-Host $paramDirectory.FullName
            $pathToSource = "$($paramDirectory.FullName)\$($path.FilePrefix)$sourceFileSuffix"
            $pathToNew = "$($paramDirectory.FullName)\$($path.FilePrefix)$destinationFileSuffix"

            Copy-Item $pathToSource -Destination $pathToNew
        }
    }
}