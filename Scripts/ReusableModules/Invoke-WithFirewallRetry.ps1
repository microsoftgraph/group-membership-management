function Invoke-WithFirewallRetry {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ResourceGroup,

        [ValidateRange(1, 10)]
        [int]$MaxRetries = 2,

        [Parameter(Mandatory = $true)]
        [scriptblock]$Operation,

        [scriptblock]$OnFirewallError
    )

    $retryCount = 0
    $result = $null

    while ($retryCount -lt $MaxRetries -and -not $result) {
        try {
            $result = & $Operation
        }
        catch {
            $errorMessage = $_.Exception.Message
            Write-Warning "❌ Error: $errorMessage"

            $messageContainsIpAddress = $errorMessage -match "\b((25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3})\b"
            if (-not $messageContainsIpAddress -and $retryCount -eq 0) {
                Write-Host "🔁 Retrying..."
            }
            elseif ($retryCount -eq 0 -and $OnFirewallError) {
                & $OnFirewallError $errorMessage
                Write-Host "🔁 Retrying after updating firewall..."
            }
            else {
                Write-Error "❌ Retry failed. Exiting."
            }
        }
        $retryCount++
    }

    return $result
}