function Invoke-AzCli {
    param (
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$OperationName,
        [switch]$AllowFailure
    )

    $stderrPath = [System.IO.Path]::GetTempFileName()
    try {
        $stdout = & az @Arguments 2>$stderrPath
        $exitCode = $LASTEXITCODE
        $stderr = (Get-Content -LiteralPath $stderrPath -Raw -ErrorAction SilentlyContinue)

        if ($AllowFailure) {
            $global:LASTEXITCODE = 0
            return [pscustomobject]@{
                ExitCode = $exitCode
                Output   = $stdout
                Error    = ($stderr | Out-String).Trim()
            }
        }

        if ($exitCode -ne 0) {
            throw "$OperationName failed (az exit code $exitCode): $(($stderr | Out-String).Trim())"
        }
        return $stdout
    }
    finally {
        Remove-Item -LiteralPath $stderrPath -Force -ErrorAction SilentlyContinue
    }
}
