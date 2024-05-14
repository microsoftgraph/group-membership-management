<#
.SYNOPSIS
Updates the ipRules.txt file with the Azure published IP ranges for the specified regions and services.
#>
param (
    [Parameter(Mandatory=$true)]
    [string]$FolderPathToSaveIpRules,
    [Parameter(Mandatory=$false)]
    [string]$Regions
)
    # Installing MSIdentityTools Module
    Write-Host "Installing MSIdentityTools Module to fetch the Azure published IP ranges..." -ForegroundColor Yellow
    Install-Module -Name MSIdentityTools -RequiredVersion 2.0.52 -Force
    Import-Module -Name MSIdentityTools
    Write-Host "Installed MSIdentityTools Module to fetch the Azure published IP ranges..." -ForegroundColor Yellow

    Write-Host "Retrieving Azure published IP ranges..." -ForegroundColor Yellow
    $allIPRanges = Get-MsIdAzureIpRange -AllServiceTagsAndRegions
    Write-Host "Retrieved Azure published IP ranges..." -ForegroundColor Yellow

    $azureSubnetProperties = $allIPRanges.values.properties

    if ([string]::IsNullOrEmpty($Regions)) {
        $regionList = $azureSubnetProperties.region `
        | Sort-Object -Unique `
        | Where-Object { $_.Contains('us') -and -not `
                        $_.Contains('australia') -and -not `
                        $_.Contains('austria') -and -not `
                        $_.Contains('euap') -and -not `
                        $_.Contains('usstag') -and -not `
                        $_.Contains('slv') -and -not `
                        $_.Contains('east') -and -not `
                        $_.Contains('central') }
    } else {
        $regionList = $Regions.Split(",") | ForEach-Object { $_.Trim() } | Sort-Object -Unique
    }


    $systemServices = @()
    $systemServices += "AzureAppService"
    $systemServices += "AzureDevOps"
    $systemServices += "DataFactory"
    $systemServices += ""

    $filteredSubnetProperties = $azureSubnetProperties | Where-Object { ($_.systemService -in $systemServices -and $_.region -in $regionList) }
    $filteredIpV4AddressPrefixes = $filteredSubnetProperties.addressPrefixes | Where-Object { $_.Contains('.') }
    $filteredIpV4AddressPrefixes = $filteredIpV4AddressPrefixes | Sort-Object -Unique
    $filteredIpV4AddressPrefixes.Count

    $filteredIpV4AddressPrefixes | Out-File "$FolderPathToSaveIpRules\ipRules.txt"
