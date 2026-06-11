function Invoke-WithFirewallRetry {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ResourceGroup,

        [ValidateRange(1, 10)]
        [int]$MaxRetries = 10,

        [Parameter(Mandatory = $true)]
        [scriptblock]$Operation,

        [scriptblock]$OnFirewallError
    )

    $retryCount = 0
    $result = $null

    while ($retryCount -lt $MaxRetries -and $result -eq $null) {
        try {
            $result = & $Operation
        }
        catch {
            $errorMessage = $_.Exception.Message
            Write-Verbose "❌ Error: $errorMessage"

            $isLastRetry = ($retryCount -eq ($MaxRetries - 1))
            $messageContainsIpAddress = $errorMessage -match "\b((25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3})\b"
            if (-not $messageContainsIpAddress -and -not $isLastRetry) {
                Write-Verbose "🔁 Not an IP address error. Retrying..."
            }
            elseif ($messageContainsIpAddress -and $OnFirewallError -and -not $isLastRetry) {
                & $OnFirewallError $errorMessage
                Write-Verbose "🔁 Retrying after updating firewall..."
            }
            elseif ($isLastRetry) {
                throw "Operation failed after $MaxRetries attempts: $errorMessage"
            }
        }
        $retryCount++
    }

    return $result
}