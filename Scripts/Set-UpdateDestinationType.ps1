$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
Creates or updates DestinationType column

.DESCRIPTION
Long description

.PARAMETER SubscriptionName
Subscription name

.PARAMETER SolutionAbbreviation
Abbreviation used to denote the overall solution

.PARAMETER EnvironmentAbbreviation
Abbreviation for the environment

.PARAMETER SourceType
Source type to map i.e. SecurityGroup

.PARAMETER DestinationType
Membership updater to use i.e. GraphUpdater

.EXAMPLE
Set-UpdateDestinationType	-SubscriptionName "<subscriptionName>"  `
                            -SolutionAbbreviation "<solution>" `
                            -EnvironmentAbbreviation "<environment>" `
						    -SourceType "<sourceType>" `
						    -DestinationType "<destinationType>" `
						    -Verbose
#>

function Set-UpdateDestinationType {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $True)]
        [string] $SubscriptionName,
        [Parameter(Mandatory = $True)]
        [string] $SolutionAbbreviation,
        [Parameter(Mandatory = $True)]
        [string] $EnvironmentAbbreviation,
        [Parameter(Mandatory = $True)]
        [string] $SourceType,
        [Parameter(Mandatory = $True)]
        [string] $DestinationType
    )

    Write-Host "Start Set-UpdateDestinationType"

    Set-AzContext -SubscriptionName $SubscriptionName

    $resourceGroupName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $storageAccounts = Get-AzStorageAccount -ResourceGroupName $resourceGroupName

    $storageAccountNamePrefix = "jobs$EnvironmentAbbreviation"
    $jobStorageAccount = $storageAccounts | Where-Object { $_.StorageAccountName -like "$storageAccountNamePrefix*" }

    if (!$jobStorageAccount) {
        Write-Host "Storage account $storageAccountNamePrefix* does not exist."
        return
    }

    $tableName = "syncJobs"
    $storageTable = Get-AzStorageTable -Name $tableName -Context $jobStorageAccount.Context -ErrorAction SilentlyContinue

    if (!$storageTable) {
        Write-Host "syncJobs table does not exist."
        return
    }

    $cloudTable = $storageTable.CloudTable
    $scriptsDirectory = Split-Path $PSScriptRoot -Parent

    . ($scriptsDirectory + '\Scripts\Install-AzTableModuleIfNeeded.ps1')
    Install-AzTableModuleIfNeeded | Out-Null

    # see https://docs.microsoft.com/en-us/rest/api/storageservices/querying-tables-and-entities#filtering-on-guid-properties
    $jobs = Get-AzTableRow -Table $cloudTable
    foreach ($job in $jobs) {

        if (!$job.DestinationType) {
            $job | Add-Member NoteProperty "DestinationType" ""
        }

        $currentQuery = ConvertFrom-Json -InputObject $job.Query
        foreach ($part in $currentQuery) {
            if ($part.type -eq $SourceType) {
                $job.DestinationType = $DestinationType
                $job | Update-AzTableRow -table $cloudTable
                break
            }
        }
    }

    Write-Host "Finish Set-UpdateDestinationType"
}