<#
    Consolidated helper functions for GMM WebAPI operations (Stop, Reschedule, Reset).
    Replaces the previously separate Stop-GMM.ps1, Reschedule-GMM.ps1, and Reset-GMM.ps1 scripts.

    Exports:
      Auth helpers:         Get-WebApiTokenWithServicePrincipal, Get-WebApiTokenWithCredentials
      Low-level operation:  Invoke-GMMWebApiOperation
      Main entry point:     Invoke-GMMOperation
      Admin utilities:Set-AppRoleToServicePrincipal, Set-WebAPIAsResetAdministrator,
                            Show-WebAPIResetAdministratorInstructions
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

# ─────────────────────────────────────────────────────────────────────────────
# Admin Utilities
# ─────────────────────────────────────────────────────────────────────────────

function Set-AppRoleToServicePrincipal {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$PrincipalId,
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$ResourceId,
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$AppRoleId
    )

    Write-Host "Setting app role to service principal with ID: $PrincipalId"

    try {
        $existingAssignments = Get-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $PrincipalId

        $assignmentExists = $existingAssignments | Where-Object {
            $_.AppRoleId -eq $AppRoleId -and $_.ResourceId -eq $ResourceId
        }

        if (-not $assignmentExists) {
            New-MgServicePrincipalAppRoleAssignment `
                -ServicePrincipalId $PrincipalId `
                -BodyParameter @{
                    principalId = $PrincipalId
                    resourceId  = $ResourceId
                    appRoleId   = $AppRoleId
                }

            Write-Host "✅ App role assignment created successfully."
        } else {
            Write-Host "App role assignment already exists. Skipping creation."
        }
    }
    finally {
        Disconnect-MgGraph -ErrorAction SilentlyContinue
    }
}

function Set-WebAPIAsResetAdministrator {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$EnvironmentAbbreviation
    )

    $scriptsDirectory = $PSScriptRoot

    Write-Host "Setting WebAPI as Reset Administrator"

    if ($global:SkipModuleInstall -ne $true) {
        . (Join-Path $scriptsDirectory 'Install-MSGraphIfNeeded.ps1')
	    Install-MSGraphIfNeeded
    }
    
    if ($global:SkipMSGraphLogin -ne $true) {
        Connect-MgGraph -Scopes "AppRoleAssignment.ReadWrite.All"
    }

    try {
        $targetRoleValue = "Operations.Reset"

        $app = Get-MgApplication -Filter "displayName eq '$SolutionAbbreviation-webapi-$EnvironmentAbbreviation'" -ErrorAction SilentlyContinue
        if (-not $app) {
            throw "❌ Application '$SolutionAbbreviation-webapi-$EnvironmentAbbreviation' not found."
        }

        $role = $app.AppRoles | Where-Object { $_.Value -eq $targetRoleValue }

        if ($role) {
            Write-Host "Role Found:"
            Write-Host "Display Name: $($role.DisplayName)"
            Write-Host "Value: $($role.Value)"
            Write-Host "ID: $($role.Id)"

            $sp = Get-MgServicePrincipal -Filter "appId eq '$($app.AppId)'" -ErrorAction SilentlyContinue
            if (-not $sp) {
                throw "❌ Service Principal for application '$($app.AppId)' not found."
            }

            Write-Host "Service Principal Found:"
            Write-Host "Display Name: $($sp.DisplayName)"
            Write-Host "ID: $($sp.Id)"

            Set-AppRoleToServicePrincipal `
                -PrincipalId $sp.Id `
                -ResourceId $sp.Id `
                -AppRoleId $role.Id
        } else {
            Write-Host "No app role found with value '$targetRoleValue'."
        }
    }
    finally {
        Disconnect-MgGraph -ErrorAction SilentlyContinue
    }
}

function Show-WebAPIResetAdministratorInstructions {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation
    )

    $scriptsDirectory = $PSScriptRoot
    $appName = "$SolutionAbbreviation-webapi-$EnvironmentAbbreviation"

    Write-Host "`n⚠️  MANUAL APP ROLE ASSIGNMENT REQUIRED" -ForegroundColor Yellow
    Write-Host "═══════════════════════════════════════════════════════════════════════════" -ForegroundColor Yellow
    Write-Host "`nThe WebAPI service principal needs to be assigned the 'Operations.Reset' app role" -ForegroundColor White
    Write-Host "to itself. This allows the WebAPI to perform reset operations.`n" -ForegroundColor White

    Write-Host "📋 Target Application:" -ForegroundColor Cyan
    Write-Host "   Application: $appName" -ForegroundColor White
    Write-Host "   Role to Assign: Operations.Reset`n" -ForegroundColor White

    Write-Host "🔧 PowerShell Script Method:" -ForegroundColor Cyan
    Write-Host "   If you prefer to run the setup script, use these commands in a separate" -ForegroundColor White
    Write-Host "   PowerShell session with appropriate permissions:`n" -ForegroundColor White

    Write-Host "   ⚠️  IMPORTANT: Install Required Modules First!" -ForegroundColor Yellow
    Write-Host "   Before running the setup script below, you must first install the required" -ForegroundColor White
    Write-Host "   PowerShell modules. Run these commands in your PowerShell session:`n" -ForegroundColor White

    Write-Host "   # Install Required Modules if you haven't already for this session" -ForegroundColor Magenta
    Write-Host "   . `"$ScriptsDirectory/Install-ModuleIfNeeded.ps1`"" -ForegroundColor Gray
    Write-Host "   Install-ModuleIfNeeded -Name `"Microsoft.Graph.Authentication`" -Version `"2.17.0`"" -ForegroundColor Gray
    Write-Host "   Install-ModuleIfNeeded -Name `"Microsoft.Graph.Applications`" -Version `"2.17.0`"" -ForegroundColor Gray
    Write-Host "   Install-ModuleIfNeeded -Name `"Microsoft.Graph.Identity.DirectoryManagement`" -Version `"2.17.0`"" -ForegroundColor Gray
    Write-Host "   Install-ModuleIfNeeded -Name `"Microsoft.Graph.Users`" -Version `"2.17.0`"" -ForegroundColor Gray
    Write-Host "" -ForegroundColor Gray
    Write-Host "   `$global:SkipModuleInstall = `$true`n" -ForegroundColor Gray
    Write-Host "   Once the modules are installed, proceed with the role assignment:`n" -ForegroundColor White

    Write-Host "   # Assign Operations.Reset Role" -ForegroundColor Green
    Write-Host "   . `"$scriptsDirectory/GMM-WebAPI-Operations.ps1`"" -ForegroundColor Gray
    Write-Host "   Set-WebAPIAsResetAdministrator ``" -ForegroundColor Gray
    Write-Host "       -SolutionAbbreviation `"$SolutionAbbreviation`" ``" -ForegroundColor Gray
    Write-Host "       -EnvironmentAbbreviation `"$EnvironmentAbbreviation`"`n" -ForegroundColor Gray

    Write-Host "📖 Azure Portal Method:" -ForegroundColor Cyan
    Write-Host "   1. Go to: https://portal.azure.com" -ForegroundColor White
    Write-Host "   2. Navigate to Microsoft Entra ID > App registrations" -ForegroundColor White
    Write-Host "   3. Find and select: $appName" -ForegroundColor White
    Write-Host "   4. Go to 'App roles' in the left menu" -ForegroundColor White
    Write-Host "   5. Verify the 'Operations.Reset' role exists" -ForegroundColor White
    Write-Host "   6. Navigate to Microsoft Entra ID > Enterprise applications" -ForegroundColor White
    Write-Host "   7. Find and select: $appName" -ForegroundColor White
    Write-Host "   8. Go to 'Permissions' in the left menu" -ForegroundColor White
    Write-Host "   9. Under 'Admin consent', locate the Operations.Reset permission" -ForegroundColor White
    Write-Host "   10. Assign the service principal to itself with the 'Operations.Reset' role`n" -ForegroundColor White
    
    Write-Host "═══════════════════════════════════════════════════════════════════════════" -ForegroundColor Yellow
    Write-Host "`n⏸️  Once you have completed the app role assignment, press ENTER to continue..." -ForegroundColor Cyan
    
    $null = Read-Host

    Write-Host "`n✅ Continuing with deployment..." -ForegroundColor Green
}
