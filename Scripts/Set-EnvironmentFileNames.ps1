<# 
.SYNOPSIS
This script rename files in bulk

.PARAMETER baseDirectory
Base directory path

.PARAMETER currentFileName
Current file name to rename

.PARAMETER newFileName
New file name

.EXAMPLE
Set-EnvironmentFileNames -baseDirectory "<base-path>" `
                         -currentFileName "parameters.<env>.json" `
                         -newFileName "parameters.<new-env>.json"
#>
function Set-EnvironmentFileNames {
    Param(
        [Parameter (Mandatory=$true)]
        [string]
        $baseDirectory,
        [Parameter (Mandatory=$true)]
        [string]
        $currentFileName,
        [Parameter (Mandatory=$true)]
        [string]
        $newFileName
    )

    $files = Get-ChildItem -Path $baseDirectory -Filter $currentFileName -Recurse

    foreach($file in $files)
    {
        Write-Host "Renaming $file to $newFileName"
        Rename-Item -Path $file -NewName $newFileName
    }
}