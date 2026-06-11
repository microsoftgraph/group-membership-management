<#
    Consolidated helper functions for GMM WebAPI operations (Stop, Reschedule, Reset).
    Replaces the previously separate Stop-GMM.ps1, Reschedule-GMM.ps1, and Reset-GMM.ps1 scripts.

    Exports:
      Auth helpers:         Get-WebApiTokenWithServicePrincipal, Get-WebApiTokenWithCredentials
      Low-level operation:  Invoke-GMMWebApiOperation
      Main entry point:     Invoke-GMMOperation
      (Admin utilities moved to Set-WebApiAzureADApplication.ps1)
#>

# ─────────────────────────────────────────────────────────────────────────────
# Shared Utilities
# ─────────────────────────────────────────────────────────────────────────────

. "$PSScriptRoot/ReusableModules/Get-KeyVaultSecretWithFirewallRetry.ps1"
. "$PSScriptRoot/ReusableModules/Invoke-WithRetry.ps1"

# ─────────────────────────────────────────────────────────────────────────────
# Auth Helpers
# ─────────────────────────────────────────────────────────────────────────────

function Get-WebApiTokenWithServicePrincipal {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$EnvironmentAbbreviation
    )

    $prereqsKeyVaultName = "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation"
    $client_id = Get-KeyVaultSecretWithFirewallRetry -VaultName $prereqsKeyVaultName -ResourceGroup $prereqsKeyVaultName -SecretName "webApiClientId" -AsPlainText
    if (-not $client_id) {
        throw "❌ Failed to retrieve webApiClientId from Key Vault."
    }
    $resource = "api://$client_id"
    $token = (Get-AzAccessToken -ResourceUrl $resource).Token
    if ($token -is [System.Security.SecureString]) {
        $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($token)
        try {
            $plainToken = [Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr)
        }
        finally {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
        }
    }
    else {
        $plainToken = $token
    }

    return $plainToken
}

function Get-WebApiTokenWithCredentials {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$EnvironmentAbbreviation
    )

    $prereqsKeyVaultName = "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation"
    $client_id = Get-KeyVaultSecretWithFirewallRetry -VaultName $prereqsKeyVaultName -ResourceGroup $prereqsKeyVaultName -SecretName "webApiClientId" -AsPlainText
    $tenant_id = Get-KeyVaultSecretWithFirewallRetry -VaultName $prereqsKeyVaultName -ResourceGroup $prereqsKeyVaultName -SecretName "webApiTenantId" -AsPlainText
    $client_secret = Get-KeyVaultSecretWithFirewallRetry -VaultName $prereqsKeyVaultName -ResourceGroup $prereqsKeyVaultName -SecretName "webApiClientSecret" -AsPlainText
    if (-not $client_id -or -not $tenant_id -or -not $client_secret) {
        throw "❌ Missing credentials. Please ensure webApiClientId, webApiTenantId, and webApiClientSecret are set in the Key Vault."
    }

    $scope = "api://$client_id/.default"
    $token_url = "https://login.microsoftonline.com/$tenant_id/oauth2/v2.0/token"
    $body = @{
        client_id     = $client_id
        client_secret = $client_secret
        scope         = $scope
        grant_type    = "client_credentials"
    }
    $response = Invoke-WithRetry -Operation {
        Invoke-RestMethod -Uri $token_url -Method Post -ContentType "application/x-www-form-urlencoded" -Body $body -ErrorAction Stop
    } -OperationName "AAD token acquisition"

    # Clear sensitive data
    $client_secret = $null
    $body = $null

    return $response.access_token
}

# ─────────────────────────────────────────────────────────────────────────────
# Generic WebAPI Operation
# ─────────────────────────────────────────────────────────────────────────────

$script:ServiceStatuses = @{
    0 = 'Running'
    1 = 'Stopped'
    2 = 'Resetting'
    3 = 'Stopping'
    4 = 'Starting'
    5 = 'Rescheduled'
    6 = 'Rescheduling'
    7 = 'Error'
}

function Invoke-GMMWebApiOperation {
    <#
    .SYNOPSIS
        Calls a GMM WebAPI operation endpoint with retry logic, then polls servicestatus
        until the expected status is reached or a timeout occurs.

    .PARAMETER OperationName
        The operation endpoint name (e.g., "Stop", "Reschedule", "Reset").

    .PARAMETER ExpectedStatus
        The numeric status code to wait for (0 = Running, 1 = Stopped).

    .PARAMETER ThrowOnTimeout
        If $true (default), throws on timeout. If $false, emits a warning and returns.
    #>
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$AccessToken,
        [Parameter(Mandatory = $true)]
        [ValidateSet("Stop", "Reschedule", "Reset")]
        [string]$OperationName,
        [Parameter(Mandatory = $true)]
        [ValidateRange(0, 7)]
        [int]$ExpectedStatus,
        [int]$MaxRetries = 3,
        [int]$MaxPollRetries = 60,
        [int]$PollIntervalSeconds = 10,
        [bool]$ThrowOnTimeout = $true
    )

    $baseUrl = "https://$SolutionAbbreviation-compute-$EnvironmentAbbreviation-webapi.azurewebsites.net/api/v1/operations"
    $headers = @{
        "Authorization" = "Bearer $AccessToken"
        "Content-Type"  = "application/json"
    }

    # POST to operation endpoint with retry
    $operationUrl = "$baseUrl/$OperationName"
    Invoke-WithRetry -Operation {
        $null = Invoke-RestMethod -Uri $operationUrl -Method POST -Headers $headers -ErrorAction Stop
    } -OperationName "$OperationName HTTP POST" -MaxAttempts ($MaxRetries + 1) -BaseDelaySeconds 2

    Start-Sleep -Seconds 15

    # Poll servicestatus until expected status
    $statusUrl = "$baseUrl/servicestatus"
    $response = Invoke-WithRetry -Operation {
        Invoke-RestMethod -Uri $statusUrl -Headers $headers -Method GET -ErrorAction Stop
    } -OperationName "$OperationName status check"
    $pollCount = 0

    while ($response.status -ne $ExpectedStatus -and $pollCount -lt $MaxPollRetries) {
        [int]$statusCode = [int]$response.status

        if ($response.status -eq 7) {
            throw "❌ $OperationName operation failed. GMM is in Error state. Please check Log Analytics for details."
        }

        $pollCount++
        Write-Host "Current service status: $($script:ServiceStatuses[$statusCode]), checking again in $PollIntervalSeconds seconds... (attempt $pollCount/$MaxPollRetries)"
        Start-Sleep -Seconds $PollIntervalSeconds
        $response = Invoke-WithRetry -Operation {
            Invoke-RestMethod -Uri $statusUrl -Headers $headers -Method GET -ErrorAction Stop
        } -OperationName "$OperationName status poll"
    }

    [int]$statusCode = [int]$response.status

    if ($response.status -eq $ExpectedStatus) {
        Write-Host "✅ GMM $OperationName completed successfully. Current service status: $($script:ServiceStatuses[$statusCode])"
    }
    else {
        $timeoutMsg = "❌ $OperationName operation timed out after $($MaxPollRetries * $PollIntervalSeconds) seconds. Current status: $($script:ServiceStatuses[$statusCode])."
        if ($ThrowOnTimeout) {
            throw "$timeoutMsg Aborting deployment."
        }
        else {
            Write-Warning "$timeoutMsg Check Log Analytics for more details."
        }
    }

    return $script:ServiceStatuses[$statusCode]
}

# ─────────────────────────────────────────────────────────────────────────────
# Main Entry Point
# ─────────────────────────────────────────────────────────────────────────────

function Invoke-GMMOperation {
    <#
    .SYNOPSIS
        High-level entry point for GMM WebAPI operations. Handles authentication,
        web app validation, and delegates to Invoke-GMMWebApiOperation.

    .PARAMETER OperationName
        The operation to perform: "Stop", "Reschedule", or "Reset".

    .PARAMETER AuthMethod
        Authentication method: "ServicePrincipal" (Az module token) or "Credentials" (client_credentials flow).
    #>
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [ValidateSet("Stop", "Reschedule", "Reset")]
        [string]$OperationName,
        [Parameter(Mandatory = $true)]
        [ValidateSet("ServicePrincipal", "Credentials")]
        [string]$AuthMethod,
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$EnvironmentAbbreviation
    )

    $resourceGroupName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation"
    $appName = "$resourceGroupName-webapi"
    $app = Get-AzWebApp -ResourceGroupName $resourceGroupName -Name $appName -ErrorAction SilentlyContinue
    
    if (-not $app) {
        throw "❌ Unable to retrieve the web app for GMM $($OperationName.ToLower())."
    }

    $token = switch ($AuthMethod) {
        "ServicePrincipal" { Get-WebApiTokenWithServicePrincipal -SolutionAbbreviation $SolutionAbbreviation -EnvironmentAbbreviation $EnvironmentAbbreviation }
        "Credentials"      { Get-WebApiTokenWithCredentials -SolutionAbbreviation $SolutionAbbreviation -EnvironmentAbbreviation $EnvironmentAbbreviation }
    }

    # Operation-specific parameters
    $operationParams = switch ($OperationName) {
        "Stop"       { @{ ExpectedStatus = 1 } }
        "Reschedule" { @{ ExpectedStatus = 0 } }
        "Reset"      { @{ ExpectedStatus = 0; MaxPollRetries = 20; PollIntervalSeconds = 30; ThrowOnTimeout = $false } }
    }

    try {
        Invoke-GMMWebApiOperation `
            -SolutionAbbreviation $SolutionAbbreviation `
            -EnvironmentAbbreviation $EnvironmentAbbreviation `
            -AccessToken $token `
            -OperationName $OperationName `
            @operationParams

        Write-Host "Call $($OperationName.ToLower()) endpoint is complete"
    }
    finally {
        $token = $null
    }
}
