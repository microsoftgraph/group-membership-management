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

# -----------------------------------------------------------------------------
# Azure Functions westUS2 hardcoded IP allowlist (temporary)
#
# These IPs were provided by the Azure Functions team as a temporary static
# list while they work on including them in the data returned by
# Get-MsIdAzureIpRange. Once those IPs surface in the upstream feed, this
# block and its append logic below should be removed.
#
# This list is appended to ipRules.txt only when -Regions includes "westus2"
# (case-insensitive). It is NOT filtered by systemService.
# -----------------------------------------------------------------------------
$AzureFunctions_WestUS2_IPS = @'
20.115.252.65/32
52.149.63.12/32
4.149.229.158/32
48.200.48.191/32
20.72.251.153/32
172.193.192.11/32
172.194.168.132/32
48.202.0.137/32
172.194.184.224/32
172.193.193.51/32
52.250.29.254/32
172.194.144.32/32
48.202.16.241/32
20.120.143.62/32
20.115.252.66/32
48.192.12.156/32
4.149.183.249/32
48.192.12.152/32
20.72.207.229/32
4.155.178.241/32
172.179.29.226/32
40.91.85.183/32
20.115.205.50/32
20.252.25.39/32
20.29.148.138/32
4.155.127.245/32
20.115.204.106/32
20.125.8.201/32
4.242.126.237/32
20.252.5.174/32
20.125.9.158/32
20.112.55.144/32
172.193.211.56/32
20.3.61.29/32
20.115.204.95/32
20.99.136.214/32
4.149.151.111/32
40.125.109.98/32
4.155.178.207/32
20.99.219.191/32
4.242.99.70/32
40.64.72.45/32
20.99.218.245/32
172.179.61.205/32
4.242.124.118/32
20.51.64.161/32
4.149.152.234/32
20.115.177.149/32
20.99.220.233/32
20.83.77.1/32
4.246.36.106/32
20.115.178.26/32
172.179.61.253/32
20.3.53.233/32
172.179.182.64/32
20.3.18.215/32
20.42.149.107/32
4.155.142.77/32
172.194.136.203/32
172.194.136.216/32
20.3.39.216/32
4.155.162.116/32
20.115.254.45/32
4.155.155.93/32
20.3.69.87/32
172.193.159.24/32
20.115.254.22/32
20.29.157.172/32
20.252.5.215/32
4.155.142.80/32
48.192.74.22/32
4.155.141.249/32
48.192.74.47/32
4.242.97.250/32
51.143.110.191/32
172.179.61.239/32
4.155.127.136/32
48.192.74.57/32
40.125.106.219/32
20.59.52.33/32
20.3.32.235/32
20.3.32.129/32
20.112.44.48/32
48.192.14.197/32
20.3.17.26/32
20.115.254.11/32
20.3.68.135/32
4.246.47.195/32
20.72.195.45/32
48.192.64.198/32
4.246.42.22/32
172.179.57.102/32
20.3.32.116/32
40.125.105.74/32
20.98.122.202/32
172.193.248.175/32
40.125.106.141/32
4.149.193.205/32
40.91.112.55/32
172.179.179.252/32
20.80.146.45/32
20.69.64.114/32
172.179.23.243/32
20.3.32.210/32
20.42.152.144/32
4.149.183.114/32
172.179.143.241/32
4.154.229.80/32
172.179.62.160/32
172.179.134.236/32
172.179.62.140/32
20.59.53.175/32
172.179.62.51/32
4.149.156.130/32
20.3.32.217/32
172.179.89.248/32
20.64.130.167/32
20.120.195.157/32
4.149.70.234/32
4.149.157.65/32
172.179.84.221/32
172.179.85.149/32
172.179.80.209/32
172.179.85.80/32
172.179.166.135/32
20.3.32.226/32
52.151.37.186/32
52.175.253.196/32
52.183.118.188/32
52.175.252.238/32
52.183.116.121/32
52.175.249.101/32
52.183.117.198/32
52.183.113.79/32
52.175.252.245/32
52.183.116.61/32
20.3.32.170/32
20.36.3.170/32
52.183.117.201/32
52.183.119.158/32
52.183.118.72/32
52.183.118.4/32
52.175.253.95/32
52.183.112.167/32
52.175.253.104/32
52.175.252.145/32
52.183.117.50/32
20.3.32.202/32
4.246.1.207/32
4.246.1.215/32
4.246.2.56/32
4.246.1.167/32
4.246.2.84/32
4.246.2.116/32
4.246.2.36/32
4.246.2.107/32
4.246.1.175/32
4.246.2.62/32
20.3.32.145/32
20.3.32.209/32
'@

# -----------------------------------------------------------------------------
# Azure Functions centralUS hardcoded IP allowlist (temporary)
#
# These IPs were provided by the Azure Functions team as a temporary static
# list while they work on including them in the data returned by
# Get-MsIdAzureIpRange. Once those IPs surface in the upstream feed, this
# block and its append logic below should be removed.
#
# This list is appended to ipRules.txt only when -Regions includes "centralus"
# (case-insensitive). It is NOT filtered by systemService.
# -----------------------------------------------------------------------------
$AzureFunctions_CentralUS_IPS = @'
172.168.204.23/32
172.168.204.184/32
172.168.204.227/32
172.168.204.105/32
172.168.204.185/32
172.168.204.9/32
172.168.204.50/32
172.168.204.51/32
172.168.204.104/32
172.168.204.22/32
128.203.221.38/32
128.203.220.215/32
128.203.220.120/32
48.214.224.11/32
128.203.221.18/32
128.203.131.108/32
128.203.131.41/32
128.203.131.117/32
128.203.131.115/32
128.203.131.55/32
128.203.129.238/32
128.203.130.144/32
128.203.131.138/32
128.203.130.114/32
128.203.131.79/32
72.152.60.156/32
72.152.60.130/32
172.169.189.175/32
72.152.60.145/32
52.242.228.32/32
130.131.180.159/32
64.236.61.178/32
72.152.60.99/32
52.242.227.238/32
64.236.62.207/32
52.242.228.79/32
52.242.228.82/32
172.169.189.137/32
172.169.189.166/32
52.230.148.26/32
128.203.216.139/32
128.203.237.99/32
132.196.115.116/32
20.15.143.150/32
132.196.120.179/32
135.233.121.213/32
135.233.120.4/32
52.230.151.77/32
172.171.29.233/32
52.182.217.119/32
132.196.114.229/32
20.84.192.39/32
40.89.250.6/32
20.9.107.77/32
20.84.248.172/32
20.80.64.222/32
20.84.192.235/32
130.131.146.161/32
52.158.211.117/32
20.84.204.99/32
20.118.17.37/32
52.158.211.124/32
20.84.131.33/32
20.221.34.177/32
20.112.194.216/32
20.84.213.82/32
20.118.10.117/32
20.112.218.220/32
13.86.4.97/32
20.84.204.158/32
130.131.145.12/32
40.89.250.170/32
20.98.187.68/32
20.40.242.197/32
135.233.71.99/32
48.214.248.232/32
48.214.248.254/32
135.233.59.57/32
13.86.3.54/32
48.214.208.235/32
20.221.89.31/32
172.169.217.45/32
172.168.170.230/32
4.249.217.125/32
52.228.165.60/32
128.203.145.178/32
172.168.107.2/32
9.234.123.191/32
172.168.243.27/32
172.168.244.6/32
9.234.76.37/32
172.168.171.81/32
52.230.224.194/32
172.168.197.169/32
172.168.92.170/32
172.168.240.157/32
4.150.144.198/32
172.169.54.72/32
52.158.223.112/32
172.168.21.208/32
172.168.253.93/32
172.168.188.17/32
20.106.0.218/32
4.249.221.243/32
20.80.113.171/32
172.168.176.207/32
20.221.117.203/32
172.169.54.211/32
4.249.167.141/32
20.12.146.98/32
9.234.74.248/32
52.143.246.86/32
72.152.46.44/32
64.236.68.14/32
52.158.223.160/32
172.168.99.138/32
20.236.250.208/32
172.169.123.208/32
9.234.76.56/32
172.168.229.6/32
4.249.156.98/32
48.214.199.255/32
20.12.173.132/32
172.168.117.218/32
20.221.14.99/32
64.236.83.52/32
172.169.9.217/32
130.131.157.160/32
64.236.66.249/32
172.168.18.174/32
4.249.240.48/32
172.169.105.124/32
172.169.123.214/32
9.234.124.36/32
20.84.171.93/32
4.150.144.243/32
4.249.160.2/32
128.203.153.56/32
128.203.216.185/32
172.168.236.213/32
9.234.122.240/32
52.143.246.94/32
172.169.29.231/32
72.152.46.41/32
128.203.153.14/32
172.168.84.103/32
72.152.63.201/32
4.249.221.242/32
52.158.223.131/32
128.203.172.175/32
4.249.167.128/32
64.236.110.224/32
172.168.67.73/32
20.15.178.157/32
64.236.56.248/32
20.112.254.251/32
52.158.223.120/32
20.112.254.24/32
172.169.35.113/32
52.230.149.175/32
20.9.110.96/32
'@

    if ($global:SkipModuleInstall -ne $true) {
		# Installing MSIdentityTools Module
        Write-Host "Installing MSIdentityTools Module to fetch the Azure published IP ranges..." -ForegroundColor Yellow
        . ($scriptsDirectory + '/Install-ModuleIfNeeded.ps1')
        Install-ModuleIfNeeded -Name MSIdentityTools -Version "2.0.52" -Verbose
        Write-Host "Installed MSIdentityTools Module to fetch the Azure published IP ranges..." -ForegroundColor Yellow

	}

    Write-Host "Retrieving Azure published IP ranges..." -ForegroundColor Yellow
    $allIPRanges = Invoke-WithRetry `
        -Operation { Get-MsIdAzureIpRange -AllServiceTagsAndRegions } `
        -OperationName "Retrieve Azure IP ranges" `
        -MaxAttempts 3 -BaseDelaySeconds 2
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

    # Build the final allowlist in memory, then write ipRules.txt once.
    $finalIpRules = $filteredIpV4AddressPrefixes

    # Append hardcoded Azure Functions westUS2 IPs when westus2 is in scope.
    # NOTE: This entire block (and the $AzureFunctions_WestUS2_IPS here-string above)
    # can be removed once the Azure Functions team includes these IPs in the
    # response of Get-MsIdAzureIpRange.
    $normalizedRegions = $regionList | ForEach-Object { $_.ToLowerInvariant() }
    if ($normalizedRegions -contains 'westus2') {
        Write-Host "westus2 detected — appending hardcoded Azure Functions westUS2 IPs to ipRules.txt..." -ForegroundColor Yellow

        $azureFunctionsWestUS2Entries = $AzureFunctions_WestUS2_IPS -split "`r?`n" `
            | ForEach-Object { $_.Trim() } `
            | Where-Object { $_ -and -not $_.StartsWith('#') }

        Write-Host "Azure Functions westUS2 hardcoded entries: $($azureFunctionsWestUS2Entries.Count)" -ForegroundColor Yellow

        $finalIpRules = @($finalIpRules) + @($azureFunctionsWestUS2Entries) | Sort-Object -Unique
    }

    # Append hardcoded Azure Functions centralUS IPs when centralus is in scope.
    # NOTE: This entire block (and the $AzureFunctions_CentralUS_IPS here-string above)
    # can be removed once the Azure Functions team includes these IPs in the
    # response of Get-MsIdAzureIpRange.
    if ($normalizedRegions -contains 'centralus') {
        Write-Host "centralus detected — appending hardcoded Azure Functions centralUS IPs to ipRules.txt..." -ForegroundColor Yellow

        $azureFunctionsCentralUSEntries = $AzureFunctions_CentralUS_IPS -split "`r?`n" `
            | ForEach-Object { $_.Trim() } `
            | Where-Object { $_ -and -not $_.StartsWith('#') }

        Write-Host "Azure Functions centralUS hardcoded entries: $($azureFunctionsCentralUSEntries.Count)" -ForegroundColor Yellow

        $finalIpRules = @($finalIpRules) + @($azureFunctionsCentralUSEntries) | Sort-Object -Unique
    }

    Write-Host "Unique IP entries written to ipRules.txt: $($finalIpRules.Count)" -ForegroundColor Yellow
    $finalIpRules | Out-File "$FolderPathToSaveIpRules/ipRules.txt"
