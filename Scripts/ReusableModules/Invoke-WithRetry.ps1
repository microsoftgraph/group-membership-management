function Test-ResultHasValue {
    param(
        [Parameter(Mandatory = $false)]
        $Value
    )

    if ($null -eq $Value) {
        return $false
    }

    if ($Value -is [string]) {
        return -not [string]::IsNullOrWhiteSpace($Value)
    }

    if ($Value -is [System.Array]) {
        return $Value.Count -gt 0
    }

    if ($Value -is [System.Collections.IEnumerable]) {
        return @($Value).Count -gt 0
    }

    return $true
}

function Invoke-WithRetry {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$Operation,

        [Parameter(Mandatory = $true)]
        [string]$OperationName,

        [ValidateRange(1, 10)]
        [int]$MaxAttempts = 3,

        [ValidateRange(1, 60)]
        [int]$BaseDelaySeconds = 2
    )

    $attempt = 1

    while ($attempt -le $MaxAttempts) {
        try {
            return & $Operation
        }
        catch {
            $isFinalAttempt = $attempt -ge $MaxAttempts
            if ($isFinalAttempt) {
                Write-Warning "'$OperationName' failed after $attempt attempt(s)."
                throw
            }

            $delaySeconds = [int]([Math]::Pow(2, $attempt - 1) * $BaseDelaySeconds)
            Write-Warning "'$OperationName' failed with transient error: $($_.Exception.Message)"
            Write-Warning "Retrying in $delaySeconds seconds (attempt $($attempt + 1)/$MaxAttempts)..."
            Start-Sleep -Seconds $delaySeconds
            $attempt++
        }
    }
}

function Invoke-WithCreateRetry {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$GetExistingOperation,

        [Parameter(Mandatory = $true)]
        [scriptblock]$CreateOperation,

        [Parameter(Mandatory = $true)]
        [string]$OperationName,

        [ValidateRange(1, 10)]
        [int]$MaxAttempts = 3,

        [ValidateRange(1, 60)]
        [int]$BaseDelaySeconds = 2,

        [Parameter(Mandatory = $false)]
        [string]$ExistsMessage
    )

    $existing = & $GetExistingOperation
    if (Test-ResultHasValue -Value $existing) {
        if ($ExistsMessage) {
            Write-Host $ExistsMessage -ForegroundColor Yellow
        }
        return $existing
    }

    $attempt = 1
    while ($attempt -le $MaxAttempts) {
        try {
            return & $CreateOperation
        }
        catch {
            $existingAfterFailure = & $GetExistingOperation
            if (Test-ResultHasValue -Value $existingAfterFailure) {
                Write-Host "'$OperationName' appears to have completed despite an error. Continuing with existing resource." -ForegroundColor Yellow
                return $existingAfterFailure
            }

            $isFinalAttempt = $attempt -ge $MaxAttempts
            if ($isFinalAttempt) {
                Write-Warning "'$OperationName' failed after $attempt attempt(s)."
                throw
            }

            $delaySeconds = [int]([Math]::Pow(2, $attempt - 1) * $BaseDelaySeconds)
            Write-Warning "'$OperationName' create operation failed with transient error: $($_.Exception.Message)"
            Write-Warning "Retrying in $delaySeconds seconds (attempt $($attempt + 1)/$MaxAttempts)..."
            Start-Sleep -Seconds $delaySeconds
            $attempt++
        }
    }
}
