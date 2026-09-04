<#
    Shared helpers for the region failover scripts. Dot-sourced by the orchestrator and by every
    step script; defines functions and constants only, so a step can be run on its own.

    FunctionAppCompat.ps1 is used instead of the Az.Functions cmdlets: Az.Functions >= 4.1.1 throws
    'Value cannot be null. (Parameter ''key'')' from every cmdlet in the module
    (Azure/azure-powershell#29630).
#>

$script:SharedScriptsDirectory = Split-Path -Parent $PSScriptRoot

. (Join-Path $script:SharedScriptsDirectory 'ReusableModules/DeploymentLogging.ps1')
. (Join-Path $script:SharedScriptsDirectory 'ReusableModules/Invoke-WithRetry.ps1')
. (Join-Path $script:SharedScriptsDirectory 'ReusableModules/Invoke-WithFirewallRetry.ps1')
. (Join-Path $script:SharedScriptsDirectory 'ReusableModules/Get-KeyVaultSecretWithFirewallRetry.ps1')
. (Join-Path $script:SharedScriptsDirectory 'ReusableModules/Set-KeyVaultSecretWithFirewallRetry.ps1')
. (Join-Path $script:SharedScriptsDirectory 'FunctionAppCompat.ps1')

# Naming token for the secondary region's resources. Names derive from the environment abbreviation
# alone, never from deployment state, which may be unreachable during the outage these scripts
# exist for.
$script:SecondaryRegionToken = 'sec'

# Read connection-string setting names. Function apps use the double-underscore form; the WebApi,
# a Microsoft.Web/sites of kind 'app', uses the colon form.
$script:ReadSettingNames = @(
    'ConnectionStrings__JobsContextReadOnly'
    'ConnectionStrings:JobsContextReadOnly'
)

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

# -----------------------------------------------------------------------------
# Output
# -----------------------------------------------------------------------------

function Write-DeploySection {
    <#
    .SYNOPSIS
        Marks a stage inside a step. Not a phase, so ##[phase] keeps meaning one step, and
        $global:GmmCurrentDeployPhase keeps pointing at the step an operator would re-run.
    #>
    param (
        [Parameter(Mandatory = $true)][string]$Name
    )

    Write-DeployLog -Level Info -Message "--- $Name ---"
}

function New-StepResult {
    <#
    .SYNOPSIS
        Builds the result object every step script returns: Ok, Blockers, State, Detail.
    #>
    param (
        [Parameter(Mandatory = $true)][string]$Step,
        [Parameter(Mandatory = $false)][bool]$Ok = $true,
        [Parameter(Mandatory = $false)][string[]]$Blockers = @(),
        [Parameter(Mandatory = $false)][hashtable]$State = @{},
        [Parameter(Mandatory = $false)][string]$Detail = ''
    )

    return [pscustomobject]@{
        Step        = $Step
        Ok          = $Ok
        Blockers    = $Blockers
        State       = $State
        Detail      = $Detail
    }
}

# -----------------------------------------------------------------------------
# Tokens
# -----------------------------------------------------------------------------

function Get-PlainTextToken {
    <#
    .SYNOPSIS
        Returns an access token as plain text. Az.Accounts 5.x returns a SecureString with no
        -AsPlainText switch; earlier versions return a string.
    #>
    param (
        [Parameter(Mandatory = $true)][AllowNull()]$Token
    )

    if ($null -eq $Token) { return $null }

    if ($Token -is [System.Security.SecureString]) {
        $pointer = [Runtime.InteropServices.Marshal]::SecureStringToGlobalAllocUnicode($Token)
        try {
            return [Runtime.InteropServices.Marshal]::PtrToStringUni($pointer)
        }
        finally {
            [Runtime.InteropServices.Marshal]::ZeroFreeGlobalAllocUnicode($pointer)
        }
    }

    return [string]$Token
}

function Get-SqlAccessToken {
    <#
    .SYNOPSIS
        Acquires an access token for Azure SQL, for use with Invoke-Sqlcmd -AccessToken.
    #>
    $token = Invoke-WithRetry `
        -Operation { Get-AzAccessToken -ResourceUrl 'https://database.windows.net/' } `
        -OperationName 'Get Azure SQL access token'

    return Get-PlainTextToken -Token $token.Token
}

# -----------------------------------------------------------------------------
# SQL
# -----------------------------------------------------------------------------

function Add-SqlIpToServerFromError {
    <#
    .SYNOPSIS
        Adds the blocked client address from a SQL firewall rejection to the named server.

    .DESCRIPTION
        ReusableModules/Invoke-SqlOperationWithFirewallRetry.ps1 composes server names from the
        solution and environment abbreviations, so it can only reach the primary region's server.
        A failover also reads from the secondary, so the target is passed in explicitly here.
    #>
    param (
        [Parameter(Mandatory = $true)][string]$ErrorMessage,
        [Parameter(Mandatory = $true)][string]$ResourceGroupName,
        [Parameter(Mandatory = $true)][string]$ServerName
    )

    $containsIpAddress = $ErrorMessage -match "\b((25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3})\b"
    if (-not $containsIpAddress) {
        Write-DeployLog -Level Warn -Message 'No client address was present in the SQL error, so no firewall rule was added.'
        return
    }

    $ipToAdd = $Matches[1]
    $ruleName = "RegionFailover_Client_IP_Address-$ipToAdd"

    $existingRule = Get-AzSqlServerFirewallRule `
        -FirewallRuleName $ruleName `
        -ResourceGroupName $ResourceGroupName `
        -ServerName $ServerName `
        -ErrorAction SilentlyContinue

    if ($null -eq $existingRule) {
        New-AzSqlServerFirewallRule `
            -ResourceGroupName $ResourceGroupName `
            -ServerName $ServerName `
            -FirewallRuleName $ruleName `
            -StartIpAddress $ipToAdd `
            -EndIpAddress $ipToAdd | Out-Null

        Write-DeployLog -Level Info -Message "Added $ipToAdd to the firewall rules on $ServerName."
    }
}

function Invoke-SqlQueryWithFirewallRetry {
    <#
    .SYNOPSIS
        Runs a query against a named server, opening the firewall for the caller if it is rejected.
    #>
    param (
        [Parameter(Mandatory = $true)][string]$ServerHostName,
        [Parameter(Mandatory = $true)][string]$ServerName,
        [Parameter(Mandatory = $true)][string]$ResourceGroupName,
        [Parameter(Mandatory = $true)][string]$DatabaseName,
        [Parameter(Mandatory = $true)][string]$Query,
        [Parameter(Mandatory = $false)][int]$MaxRetries = 5
    )

    return Invoke-WithFirewallRetry -ResourceGroup $ResourceGroupName -MaxRetries $MaxRetries `
        -Operation {
            $accessToken = Get-SqlAccessToken
            try {
                Invoke-Sqlcmd -ServerInstance $ServerHostName -Database $DatabaseName `
                    -AccessToken $accessToken -Query $Query -ErrorAction Stop
            }
            finally {
                $accessToken = $null
            }
        } `
        -OnFirewallError {
            param($firewallErrorMessage)
            Add-SqlIpToServerFromError -ErrorMessage $firewallErrorMessage `
                -ResourceGroupName $ResourceGroupName -ServerName $ServerName
        }
}

# -----------------------------------------------------------------------------
# Naming
# -----------------------------------------------------------------------------

function Get-GmmContext {
    <#
    .SYNOPSIS
        Derives every resource name the failover needs from the environment abbreviation.

    .DESCRIPTION
        The two read-replica servers are named asymmetrically - region B's carries the region
        token, region A's does not - so no single formula addresses both:

            Secondary -> <solution>-data-<env>-sec-R
            Primary   -> <solution>-data-<env>-R

        That is why -TargetRegion names a role, and why fail-back works with the same scripts.
    #>
    param (
        [Parameter(Mandatory = $true)][string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)][ValidateSet('Primary', 'Secondary')][string]$TargetRegion,
        [Parameter(Mandatory = $false)][string]$SolutionAbbreviation = 'gmm'
    )

    $azureContext = Get-AzContext
    if (-not $azureContext) {
        throw 'No Azure context is available. Run Connect-AzAccount before running a failover.'
    }

    $sqlHostSuffix = (Get-AzEnvironment -Name $azureContext.Environment.Name).SqlDatabaseDnsSuffix

    $dataResourceGroupName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $primaryServerName = $dataResourceGroupName
    $secondaryServerName = "$primaryServerName-$script:SecondaryRegionToken"

    $targetReplicaServerName = if ($TargetRegion -eq 'Secondary') {
        "$primaryServerName-$script:SecondaryRegionToken-R"
    }
    else {
        "$primaryServerName-R"
    }

    $promotionServerName = if ($TargetRegion -eq 'Secondary') { $secondaryServerName } else { $primaryServerName }
    $promotionResourceGroupName = if ($TargetRegion -eq 'Secondary') { $secondaryServerName } else { $dataResourceGroupName }

    $failoverGroupName = "$primaryServerName-fog"

    return [pscustomobject]@{
        SqlHostSuffix              = $sqlHostSuffix
        DataResourceGroupName      = $dataResourceGroupName
        ComputeResourceGroupName   = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation"
        PrereqsKeyVaultName        = "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation"
        KeyVaultName               = $dataResourceGroupName
        PrimaryServerName          = $primaryServerName
        PromotionServerName        = $promotionServerName
        PromotionResourceGroupName = $promotionResourceGroupName
        TargetReplicaServerName    = $targetReplicaServerName
        FailoverGroupName          = $failoverGroupName
        ListenerHostName           = "$failoverGroupName$sqlHostSuffix"
        JobsDatabaseName           = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
        ReplicaDatabaseName        = "$SolutionAbbreviation-data-$EnvironmentAbbreviation-R"
        ReadSecretName             = 'replicaJobsMSIConnectionString'
        WebApiSiteName             = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-webapi"
    }
}

# -----------------------------------------------------------------------------
# GMM WebApi operations
# -----------------------------------------------------------------------------

function Get-WebApiCredential {
    <#
    .SYNOPSIS
        Reads the WebApi application's credentials from the prereqs Key Vault, once per process.

    .DESCRIPTION
        Every step that talks to the WebApi needs these, and the steps are deliberately independent
        of one another, so without a cache a single run reads the same three secrets repeatedly.
        The values are cached, not the token, so token lifetime is unaffected.
    #>
    param (
        [Parameter(Mandatory = $true)][string]$PrereqsKeyVaultName
    )

    if ($script:WebApiCredentialCache -and $script:WebApiCredentialCache.VaultName -eq $PrereqsKeyVaultName) {
        return $script:WebApiCredentialCache
    }

    $clientId = Get-KeyVaultSecretWithFirewallRetry -VaultName $PrereqsKeyVaultName `
        -ResourceGroup $PrereqsKeyVaultName -SecretName 'webApiClientId' -AsPlainText

    if ([string]::IsNullOrWhiteSpace($clientId)) {
        throw "Unable to read webApiClientId from Key Vault '$PrereqsKeyVaultName'."
    }

    $tenantId = $null
    $clientSecret = $null
    try {
        $tenantId = Get-KeyVaultSecretWithFirewallRetry -VaultName $PrereqsKeyVaultName `
            -ResourceGroup $PrereqsKeyVaultName -SecretName 'webApiTenantId' -AsPlainText

        $clientSecret = Get-KeyVaultSecretWithFirewallRetry -VaultName $PrereqsKeyVaultName `
            -ResourceGroup $PrereqsKeyVaultName -SecretName 'webApiClientSecret' -AsPlainText
    }
    catch {
        Write-DeployLog -Level Warn -Message 'Client credentials are unavailable in the prereqs Key Vault; the delegated token will be used instead.'
    }

    $script:WebApiCredentialCache = [pscustomobject]@{
        VaultName    = $PrereqsKeyVaultName
        ClientId     = $clientId
        TenantId     = $tenantId
        ClientSecret = $clientSecret
    }

    return $script:WebApiCredentialCache
}

function Get-WebApiAccessToken {
    <#
    .SYNOPSIS
        Acquires a WebApi access token, preferring the client-credentials flow.

    .DESCRIPTION
        The delegated path alone is not sufficient: requesting 'api://<webApiClientId>' fails for an
        operator signed in as themselves, because the signing-in client is not pre-authorized for
        the WebApi application (AADSTS500011). These scripts are operator-run.
    #>
    param (
        [Parameter(Mandatory = $true)][string]$PrereqsKeyVaultName
    )

    $credential = Get-WebApiCredential -PrereqsKeyVaultName $PrereqsKeyVaultName

    if (-not [string]::IsNullOrWhiteSpace($credential.TenantId) -and -not [string]::IsNullOrWhiteSpace($credential.ClientSecret)) {
        $body = @{
            client_id     = $credential.ClientId
            client_secret = $credential.ClientSecret
            scope         = "api://$($credential.ClientId)/.default"
            grant_type    = 'client_credentials'
        }

        try {
            $response = Invoke-WithRetry `
                -Operation {
                    Invoke-RestMethod `
                        -Uri "https://login.microsoftonline.com/$($credential.TenantId)/oauth2/v2.0/token" `
                        -Method Post -ContentType 'application/x-www-form-urlencoded' -Body $body
                } `
                -OperationName 'Acquire a WebApi access token with client credentials'

            return $response.access_token
        }
        finally {
            $body = $null
        }
    }

    $delegatedToken = Invoke-WithRetry `
        -Operation { Get-AzAccessToken -ResourceUrl "api://$($credential.ClientId)" } `
        -OperationName 'Acquire a WebApi access token'

    return Get-PlainTextToken -Token $delegatedToken.Token
}

function Get-OperationsBaseUrl {
    param (
        [Parameter(Mandatory = $true)][pscustomobject]$Context
    )

    $webApi = Invoke-WithRetry `
        -Operation { Get-AzWebApp -ResourceGroupName $Context.ComputeResourceGroupName -Name $Context.WebApiSiteName } `
        -OperationName "Resolve the WebApi hostname for $($Context.WebApiSiteName)"

    if (-not $webApi -or [string]::IsNullOrWhiteSpace($webApi.DefaultHostName)) {
        throw "Unable to resolve the hostname for '$($Context.WebApiSiteName)' in '$($Context.ComputeResourceGroupName)'."
    }

    return "https://$($webApi.DefaultHostName)/api/v1/operations"
}

function Get-GmmServiceStatus {
    <#
    .SYNOPSIS
        Reads GMM's service status.

    .DESCRIPTION
        Retries, because the WebApi recycles whenever its own app settings change and refuses
        connections until it has restarted. Callers that run immediately after a repoint should
        raise MaxAttempts.
    #>
    param (
        [Parameter(Mandatory = $true)][string]$BaseUrl,
        [Parameter(Mandatory = $true)][string]$Token,
        [Parameter(Mandatory = $false)][int]$MaxAttempts = 3,
        [Parameter(Mandatory = $false)][int]$BaseDelaySeconds = 2
    )

    $headers = @{ 'Authorization' = "Bearer $Token"; 'Content-Type' = 'application/json' }
    # Captured before the scriptblock, under a name Invoke-WithRetry does not itself declare.
    $statusUri = "$BaseUrl/servicestatus"

    $response = Invoke-WithRetry `
        -Operation { Invoke-RestMethod -Uri $statusUri -Method GET -Headers $headers } `
        -OperationName 'Read the GMM service status' `
        -MaxAttempts $MaxAttempts -BaseDelaySeconds $BaseDelaySeconds

    return [int]$response.status
}

function Invoke-GmmOperation {
    <#
    .SYNOPSIS
        Posts a GMM WebApi operation and polls servicestatus until the expected status is reached.
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param (
        [Parameter(Mandatory = $true)][ValidateSet('Stop', 'Start')][string]$OperationName,
        [Parameter(Mandatory = $true)][int]$ExpectedStatus,
        [Parameter(Mandatory = $true)][string]$BaseUrl,
        [Parameter(Mandatory = $true)][string]$PrereqsKeyVaultName,
        [Parameter(Mandatory = $false)][int]$MaxPollAttempts = 60,
        [Parameter(Mandatory = $false)][int]$PollIntervalSeconds = 10
    )

    $expectedName = $script:ServiceStatuses[$ExpectedStatus]
    Write-DeployLog -Level Info -Message "Operation: POST $BaseUrl/$OperationName (waiting for service status '$expectedName')"

    if (-not $PSCmdlet.ShouldProcess('GMM', "POST api/v1/operations/$OperationName")) {
        Write-DeployLog -Level Info -Message "WhatIf: GMM would be asked to $($OperationName.ToLower()) and polled until it reported '$expectedName'."
        return $expectedName
    }

    $token = Get-WebApiAccessToken -PrereqsKeyVaultName $PrereqsKeyVaultName
    try {
        $headers = @{ 'Authorization' = "Bearer $token"; 'Content-Type' = 'application/json' }

        # Repointing the WebApi's own read setting recycles the app, so a request issued immediately
        # afterwards fails until it has restarted - hence the retry below.
        #
        # Build the URI BEFORE the scriptblock. A scriptblock passed to Invoke-WithRetry resolves
        # its variables against that function's scope, where $OperationName is Invoke-WithRetry's
        # OWN parameter - the retry description, not 'Stop'. Referencing it inside the scriptblock
        # posts to a malformed URI and the WebApi answers 400 on every attempt.
        $operationUri = "$BaseUrl/$OperationName"

        Invoke-WithRetry `
            -Operation { $null = Invoke-RestMethod -Uri $operationUri -Method POST -Headers $headers } `
            -OperationName "POST the $OperationName operation (the WebApi may still be recycling)" `
            -MaxAttempts 6 -BaseDelaySeconds 5

        Start-Sleep -Seconds 15

        $attempt = 0
        do {
            $statusCode = Get-GmmServiceStatus -BaseUrl $BaseUrl -Token $token

            if ($statusCode -eq 7) {
                throw "GMM reported the Error service status while running the $OperationName operation. Check Log Analytics."
            }

            if ($statusCode -eq $ExpectedStatus) {
                Write-DeployLog -Level Success -Message "GMM service status: $($script:ServiceStatuses[$statusCode])"
                return $script:ServiceStatuses[$statusCode]
            }

            $attempt++
            Write-DeployLog -Level Info -Message "GMM service status: $($script:ServiceStatuses[$statusCode]); polling again in $PollIntervalSeconds seconds ($attempt/$MaxPollAttempts)"
            Start-Sleep -Seconds $PollIntervalSeconds
        } while ($attempt -lt $MaxPollAttempts)

        throw "The $OperationName operation timed out after $($MaxPollAttempts * $PollIntervalSeconds) seconds without reaching '$expectedName'."
    }
    finally {
        $token = $null
    }
}
