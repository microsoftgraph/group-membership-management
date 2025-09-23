function Add-SqlIpFromError {
    param (
        [string]$ErrorMessage,
        [string]$SolutionAbbreviation,
        [string]$EnvironmentAbbreviation
    )

    $messageContainsIpAddress = $ErrorMessage -match "\b((25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3})\b"
    if ($messageContainsIpAddress) {
        $ipToAdd = $matches[1]
        Write-Host "Extracted IP: $ipToAdd"

        Write-Host "`nSetting SQL Server firewall from error message"
        $dataResourceGroupName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
        $sqlServerName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"

        $sqlIPRuleName = "DeploymentScript_Client_IP_Address-$ipToAdd"
        $sqlIPRule = Get-AzSqlServerFirewallRule `
            -FirewallRuleName $sqlIPRuleName `
            -ResourceGroupName $dataResourceGroupName `
            -ServerName $sqlServerName `
            -ErrorAction SilentlyContinue

        if ($null -eq $sqlIPRule) {
            New-AzSqlServerFirewallRule `
                -ResourceGroupName $dataResourceGroupName `
                -ServerName $sqlServerName `
                -FirewallRuleName $sqlIPRuleName `
                -StartIpAddress $ipToAdd `
                -EndIpAddress $ipToAdd
            Write-Host "Added Sql IP $ipToAdd to SQL Server firewall rules."
        }
    }
    else {
        Write-Warning "⚠️ No IP address found in the error message."
    }
}

function Invoke-SqlOperationWithFirewallRetry {
    param (
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)]
        [scriptblock]$Operation,
        [Parameter(Mandatory = $false)]
        [int]$MaxRetries = 3
    )
    
    $directory = $PSScriptRoot
    . ($directory + '\Invoke-WithFirewallRetry.ps1')

    Invoke-WithFirewallRetry -ResourceGroup $dataResourceGroup -MaxRetries $MaxRetries `
            -Operation $Operation `
            -OnFirewallError {
                param($errorMessage) 
                Add-SqlIpFromError -ErrorMessage $errorMessage -SolutionAbbreviation $SolutionAbbreviation -EnvironmentAbbreviation $EnvironmentAbbreviation
            }
}