<#
.SYNOPSIS
    Promotes GMM's database failover group to the target region and repoints the read endpoint.

.DESCRIPTION
    The database tier of the region failover. One step, which internally promotes the failover
    group, verifies the read-write listener CNAME has followed, writes a new version of the read
    connection-string secret, repoints that setting on every site carrying it, reports configuration
    drift, and - after a forced promotion only - resets sync jobs stranded in InProgress.

    The write connection string needs no change: it already addresses the failover-group listener,
    which follows the promotion. Only the read endpoint moves, because the read replicas are
    addressed directly by server name.

    Promoting the GROUP covers every database in it, which is why this is not named after any single
    database. Only the read-endpoint handling is SyncJobs-specific.

.EXAMPLE
    ./Invoke-DatabaseFailover.ps1 -EnvironmentAbbreviation <env> -TargetRegion Secondary -WhatIf
#>
[CmdletBinding(SupportsShouldProcess)]
param (
    [Parameter(Mandatory = $true)][ValidateNotNullOrEmpty()][string]$EnvironmentAbbreviation,
    [Parameter(Mandatory = $true)][ValidateSet('Primary', 'Secondary')][string]$TargetRegion,
    [Parameter(Mandatory = $false)][ValidateNotNullOrEmpty()][string]$SolutionAbbreviation = 'gmm',
    [Parameter(Mandatory = $false)][switch]$Force
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')

$stepName = 'Databases'

# -----------------------------------------------------------------------------
# Helpers
# -----------------------------------------------------------------------------

function Get-FailoverGroup {
    param (
        [Parameter(Mandatory = $true)][pscustomobject]$Context
    )

    return Invoke-WithRetry `
        -Operation {
            Get-AzSqlDatabaseFailoverGroup `
                -ResourceGroupName $Context.DataResourceGroupName `
                -ServerName $Context.PrimaryServerName `
                -FailoverGroupName $Context.FailoverGroupName
        } `
        -OperationName "Read the failover group $($Context.FailoverGroupName)"
}

function Get-DataGoodThroughUtc {
    <#
    .SYNOPSIS
        Reads the point in time through which the promotion target holds committed data.

    .DESCRIPTION
        sys.dm_continuous_copy_status.last_commit, read from the secondary itself. It must come from
        the secondary: last_replication in sys.dm_geo_replication_link_status is populated only on a
        primary, and in the disaster this exists for the primary is unreachable. Filtering on
        is_target_role makes a database that is already primary return nothing rather than a value
        meaning something else.

        Best effort - a failure here is reported but does not stop the failover.
    #>
    param (
        [Parameter(Mandatory = $true)][pscustomobject]$Context
    )

    $query = @"
SELECT TOP 1
       last_commit            AS DataGoodThrough,
       replication_state_desc AS ReplicationState,
       is_rpo_limit_reached   AS RpoLimitReached
FROM sys.dm_continuous_copy_status
WHERE is_target_role = 1
"@

    $serverHostName = "$($Context.PromotionServerName)$($Context.SqlHostSuffix)"

    if (-not (Get-Command -Name 'Invoke-Sqlcmd' -ErrorAction SilentlyContinue)) {
        Write-DeployLog -Level Warn -Message 'The SqlServer module is not available, so the data-loss boundary could not be read.'
        Write-DeployLog -Level Warn -Message "Run the following against $serverHostName / $($Context.JobsDatabaseName) BEFORE promoting, and record the result:"
        Write-DeployLog -Level Warn -Message $query
        return $null
    }

    try {
        $row = Invoke-SqlQueryWithFirewallRetry `
            -ServerHostName $serverHostName `
            -ServerName $Context.PromotionServerName `
            -ResourceGroupName $Context.PromotionResourceGroupName `
            -DatabaseName $Context.JobsDatabaseName `
            -Query $query | Select-Object -First 1

        if ($null -eq $row -or $null -eq $row.DataGoodThrough -or $row.DataGoodThrough -is [DBNull]) {
            Write-DeployLog -Level Warn -Message "The data-loss boundary could not be determined: $serverHostName reported no replication target for '$($Context.JobsDatabaseName)'. Confirm it is the geo-secondary being promoted."
            return $null
        }

        $boundary = if ($row.DataGoodThrough -is [DateTimeOffset]) { $row.DataGoodThrough.UtcDateTime } else { [datetime]$row.DataGoodThrough }

        return [pscustomobject]@{
            DataGoodThroughUtc = $boundary
            ReplicationState   = [string]$row.ReplicationState
            RpoLimitReached    = [bool]$row.RpoLimitReached
        }
    }
    catch {
        Write-DeployLog -Level Warn -Message "The data-loss boundary could not be read from $serverHostName ($($_.Exception.Message)). The failover continues; treat the recoverable point as unknown."
        return $null
    }
}

function Test-ListenerResolution {
    param (
        [Parameter(Mandatory = $true)][string]$ListenerHostName,
        [Parameter(Mandatory = $true)][string]$ExpectedServerName,
        [Parameter(Mandatory = $false)][int]$MaxAttempts = 12,
        [Parameter(Mandatory = $false)][int]$DelaySeconds = 10
    )

    # The listener is a CNAME with a 30 second TTL. A forced promotion returns in about 14 seconds,
    # inside that TTL, so the stale record must be allowed to expire before GMM is restarted.
    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        $records = @()
        try {
            $records = Resolve-DnsName -Name $ListenerHostName -Type CNAME -DnsOnly -ErrorAction Stop |
                Where-Object { $_.NameHost } | Select-Object -ExpandProperty NameHost
        }
        catch {
            Write-DeployLog -Level Info -Message "DNS lookup attempt $attempt/$MaxAttempts failed: $($_.Exception.Message)"
        }

        foreach ($record in $records) {
            Write-DeployLog -Level Info -Message "Listener $ListenerHostName resolves to $record"
            if ($record -like "$ExpectedServerName.*") { return $true }
        }

        if ($attempt -lt $MaxAttempts) { Start-Sleep -Seconds $DelaySeconds }
    }

    return $false
}

function Set-ReadConnectionSecretVersion {
    <#
    .SYNOPSIS
        Writes a new version of the read connection-string secret and returns its versioned URI.
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param (
        [Parameter(Mandatory = $true)][string]$KeyVaultName,
        [Parameter(Mandatory = $true)][string]$SecretName,
        [Parameter(Mandatory = $true)][string]$SecretValue
    )

    if (-not $PSCmdlet.ShouldProcess("Key Vault $KeyVaultName", "write a new version of secret '$SecretName'")) {
        $current = Get-KeyVaultSecretWithFirewallRetry -VaultName $KeyVaultName `
            -ResourceGroup $KeyVaultName -SecretName $SecretName
        Write-DeployLog -Level Info -Message "WhatIf: a new version of '$SecretName' would be written; the current version is $($current.Version)."
        return $current.Id
    }

    $secret = Set-KeyVaultSecretWithFirewallRetry -VaultName $KeyVaultName `
        -ResourceGroup $KeyVaultName -SecretName $SecretName -SecretValue $SecretValue

    return $secret.Id
}

function Get-ReadEndpointSite {
    <#
    .SYNOPSIS
        Finds every site in the compute resource group that carries a read connection-string setting.

    .DESCRIPTION
        Discovery rather than a hardcoded list, because the applicable set differs by environment
        and SqlMembershipObtainer has no read context and must not be repointed.

        Settings are read with Get-AzWebApp for every site, function app or not. That is a plain GET
        and so still runs under -WhatIf, whereas FunctionAppCompat's equivalent read is a REST POST,
        which Invoke-AzRestMethod suppresses under -WhatIf - which would break discovery and take
        the whole rehearsal with it. FunctionAppCompat is still used for the write.
    #>
    param (
        [Parameter(Mandatory = $true)][string]$ComputeResourceGroupName
    )

    $sites = Invoke-WithRetry `
        -Operation { Get-AzResource -ResourceGroupName $ComputeResourceGroupName -ResourceType 'Microsoft.Web/sites' } `
        -OperationName "List sites in $ComputeResourceGroupName"

    $applicableSites = @()

    foreach ($site in ($sites | Sort-Object -Property Name)) {
        $app = Invoke-WithRetry `
            -Operation { Get-AzWebApp -ResourceGroupName $ComputeResourceGroupName -Name $site.Name } `
            -OperationName "Read app settings for $($site.Name)"

        $settingNames = $app.SiteConfig.AppSettings.Name
        $readSettingName = $script:ReadSettingNames | Where-Object { $settingNames -contains $_ } | Select-Object -First 1

        if ($readSettingName) {
            # Carry the settings discovery already read, so the write path need not GET again.
            $currentSettings = @{}
            foreach ($setting in $app.SiteConfig.AppSettings) {
                $currentSettings[$setting.Name] = $setting.Value
            }

            $applicableSites += [pscustomobject]@{
                Name            = $site.Name
                IsFunctionApp   = $site.Kind -like '*functionapp*'
                ReadSettingName = $readSettingName
                CurrentSettings = $currentSettings
            }
        }
    }

    return $applicableSites
}

function Update-ReadEndpointSetting {
    <#
    .SYNOPSIS
        Repoints one site's read connection-string setting at the supplied versioned secret URI.

    .DESCRIPTION
        Both paths are read-modify-write, so no unrelated setting is disturbed. Function apps merge
        through Update-FunctionAppSettingCompat; the WebApi is kind 'app' and goes through
        Set-AzWebApp, which REPLACES the whole collection, so it is rehydrated first from the set
        Get-ReadEndpointSite already read.
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param (
        [Parameter(Mandatory = $true)][pscustomobject]$Site,
        [Parameter(Mandatory = $true)][string]$ComputeResourceGroupName,
        [Parameter(Mandatory = $true)][string]$VersionedSecretUri
    )

    $settingValue = "@Microsoft.KeyVault(SecretUri=$VersionedSecretUri)"
    $targetVersion = Split-Path -Path $VersionedSecretUri -Leaf

    if (-not $PSCmdlet.ShouldProcess($Site.Name, "set '$($Site.ReadSettingName)' to secret version $targetVersion")) {
        Write-DeployLog -Level Info -Message "WhatIf: $($Site.Name) - '$($Site.ReadSettingName)' would be repointed to version $targetVersion."
        return $null
    }

    $resolvedValue = $null

    if ($Site.IsFunctionApp) {
        $updatedSettings = Invoke-WithRetry `
            -Operation {
                Update-FunctionAppSettingCompat -ResourceGroupName $ComputeResourceGroupName `
                    -Name $Site.Name -AppSetting @{ $Site.ReadSettingName = $settingValue }
            } `
            -OperationName "Repoint the read setting on $($Site.Name)"

        $resolvedValue = $updatedSettings[$Site.ReadSettingName]
    }
    else {
        # Rehydrate from the collection discovery already read. Safe because GMM is stopped for the
        # whole step, so nothing else writes these settings.
        $updatedSettings = @{}
        foreach ($name in $Site.CurrentSettings.Keys) {
            $updatedSettings[$name] = $Site.CurrentSettings[$name]
        }
        $updatedSettings[$Site.ReadSettingName] = $settingValue

        $null = Invoke-WithRetry `
            -Operation { Set-AzWebApp -ResourceGroupName $ComputeResourceGroupName -Name $Site.Name -AppSettings $updatedSettings } `
            -OperationName "Repoint the read setting on $($Site.Name)"

        $verified = Invoke-WithRetry `
            -Operation { Get-AzWebApp -ResourceGroupName $ComputeResourceGroupName -Name $Site.Name } `
            -OperationName "Verify the read setting on $($Site.Name)"

        $resolvedValue = ($verified.SiteConfig.AppSettings | Where-Object { $_.Name -eq $Site.ReadSettingName }).Value
    }

    if ($resolvedValue -match 'SecretUri=([^)]+)') {
        return (Split-Path -Path $Matches[1] -Leaf)
    }

    return 'unresolved'
}

function Write-ConfigurationDriftReport {
    param (
        [Parameter(Mandatory = $true)][string]$ActiveRegionRole,
        [Parameter(Mandatory = $true)][string]$ReplicaServerName,
        [Parameter(Mandatory = $true)][string]$SecretName,
        [Parameter(Mandatory = $true)][string]$SecretVersion,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Sites
    )

    Write-DeploySection -Name 'Configuration drift'
    Write-DeployLog -Level Info -Message "Active region role:  $ActiveRegionRole"
    Write-DeployLog -Level Info -Message "Read replica server: $ReplicaServerName"
    Write-DeployLog -Level Info -Message "Secret:              $SecretName (version $SecretVersion)"
    Write-DeployLog -Level Info -Message "Sites repointed:     $($Sites.Count)"

    foreach ($site in $Sites) {
        Write-DeployLog -Level Info -Message ("{0,-52} {1,-42} version {2}" -f $site.Name, $site.ReadSettingName, $site.ResolvedSecretVersion)
    }

    if ($ActiveRegionRole -eq 'Primary') {
        Write-DeployLog -Level Success -Message 'The read app settings now match the values the deployment templates generate. No drift remains.'
        return
    }

    Write-DeployLog -Level Warn -Message 'Intentional configuration drift is now in effect.'
    Write-DeployLog -Level Warn -Message "The read app settings above point at '$ReplicaServerName', while the deployment templates generate region A's replica server."
    Write-DeployLog -Level Warn -Message 'A subsequent deployment will silently revert these settings and re-assert region A as the replication source - no error and no warning will be raised.'
    Write-DeployLog -Level Warn -Message 'Operating rule: fail back with -TargetRegion Primary before deploying. If a deploy must happen while failed over, re-run this step afterwards.'
}

# -----------------------------------------------------------------------------
# Main
# -----------------------------------------------------------------------------

try {
    $context = Get-GmmContext -EnvironmentAbbreviation $EnvironmentAbbreviation -TargetRegion $TargetRegion -SolutionAbbreviation $SolutionAbbreviation

    # ----- execute -------------------------------------------------------------
    Write-DeployPhase -Name 'Step 2 - Database failover' -Event Begin

    # Database-specific context belongs here rather than in the orchestrator's header, which should
    # carry only information that is true of the whole run regardless of which tiers exist.
    Write-DeployLog -Level Info -Message "Promoting server:   $($context.PromotionServerName) (resource group $($context.PromotionResourceGroupName))"
    Write-DeployLog -Level Info -Message "Failover group:     $($context.FailoverGroupName)"
    Write-DeployLog -Level Info -Message "Write listener:     $($context.ListenerHostName)"
    Write-DeployLog -Level Info -Message "Read replica:       $($context.TargetReplicaServerName)"

    Write-DeploySection -Name 'Promotion'

    $group = Get-FailoverGroup -Context $context
    $activeRole = if ($group.ReplicationRole -eq 'Primary') { 'Primary' } else { 'Secondary' }

    $dataGoodThrough = $null
    $lagSeconds = $null
    if ($Force) {
        # Read the boundary BEFORE promoting: afterwards the role changes and the value no longer
        # describes the pre-failover state.
        Write-DeployLog -Level Info -Message 'Forced failover - reading the recoverable data boundary.'
        if ($PSCmdlet.ShouldProcess("$($context.PromotionServerName) / $($context.JobsDatabaseName)", 'read the recoverable data boundary')) {
            $boundaryInfo = Get-DataGoodThroughUtc -Context $context
            if ($null -ne $boundaryInfo) {
                $dataGoodThrough = $boundaryInfo.DataGoodThroughUtc

                # Report the gap as well as the instant, so an operator does not have to subtract
                # two UTC timestamps during an incident. Measured against this host's clock, so it
                # is an approximation, not an RPO guarantee; a negative value means the clocks
                # disagree.
                $lagSeconds = [math]::Round(((Get-Date).ToUniversalTime() - $dataGoodThrough).TotalSeconds, 1)
                $lagText = if ($lagSeconds -lt 0) {
                    "$([math]::Abs($lagSeconds))s AHEAD of this host's clock - clocks disagree, treat the boundary as approximate"
                }
                else {
                    "$lagSeconds seconds behind at the moment of promotion"
                }

                Write-DeployLog -Level Info -Message "DataGoodThrough (UTC):  $($dataGoodThrough.ToString('yyyy-MM-ddTHH:mm:ss.fffZ'))"
                Write-DeployLog -Level Info -Message "Replication lag:        $lagText"
                Write-DeployLog -Level Info -Message "Replication state:      $($boundaryInfo.ReplicationState)"
                Write-DeployLog -Level Info -Message "RPO limit reached:      $($boundaryInfo.RpoLimitReached)"
                Write-DeployLog -Level Warn -Message "Writes committed in the primary region after $($dataGoodThrough.ToString('yyyy-MM-ddTHH:mm:ss.fffZ')) are not present on $($context.PromotionServerName) and are lost by this promotion."
            }
            else {
                Write-DeployLog -Level Warn -Message 'DataGoodThrough (UTC): unknown - the boundary could not be read.'
            }
        }
        else {
            Write-DeployLog -Level Info -Message "WhatIf: the recoverable data boundary would be read from $($context.PromotionServerName) before promoting."
        }
    }

    if ($activeRole -eq $TargetRegion) {
        Write-DeployLog -Level Info -Message "The failover group is already primary in the $TargetRegion role; skipping the promotion."
    }
    else {
        $promotionDescription = if ($Force) {
            "Switch-AzSqlDatabaseFailoverGroup -AllowDataLoss on $($context.PromotionServerName)"
        }
        else {
            "Switch-AzSqlDatabaseFailoverGroup on $($context.PromotionServerName)"
        }
        Write-DeployLog -Level Info -Message "Promotion: $promotionDescription"

        if ($PSCmdlet.ShouldProcess("failover group $($context.FailoverGroupName)", "promote $($context.PromotionServerName) to primary")) {
            $null = Invoke-WithRetry `
                -Operation {
                    if ($Force) {
                        Switch-AzSqlDatabaseFailoverGroup `
                            -ResourceGroupName $context.PromotionResourceGroupName `
                            -ServerName $context.PromotionServerName `
                            -FailoverGroupName $context.FailoverGroupName `
                            -AllowDataLoss
                    }
                    else {
                        Switch-AzSqlDatabaseFailoverGroup `
                            -ResourceGroupName $context.PromotionResourceGroupName `
                            -ServerName $context.PromotionServerName `
                            -FailoverGroupName $context.FailoverGroupName
                    }
                } `
                -OperationName 'Failover group promotion' `
                -MaxAttempts 2 -BaseDelaySeconds 10

            Write-DeployLog -Level Success -Message "Promotion completed. '$($context.PromotionServerName)' is now the read-write primary."
        }
        else {
            Write-DeployLog -Level Info -Message 'WhatIf: the promotion above would run now.'
        }
    }

    # ----- verify the listener -------------------------------------------------
    Write-DeploySection -Name 'Read-write listener'
    Write-DeployLog -Level Info -Message "Expecting $($context.ListenerHostName) to resolve to $($context.PromotionServerName)"

    if ($WhatIfPreference) {
        Write-DeployLog -Level Info -Message "WhatIf: $($context.ListenerHostName) would be resolved until it pointed at $($context.PromotionServerName)."
    }
    elseif (-not (Test-ListenerResolution -ListenerHostName $context.ListenerHostName -ExpectedServerName $context.PromotionServerName)) {
        throw "HALTED: $($context.ListenerHostName) did not resolve to $($context.PromotionServerName). GMM must not be restarted until it does, or writes will land on the demoted, read-only server."
    }
    else {
        Write-DeployLog -Level Success -Message "Listener $($context.ListenerHostName) now resolves to $($context.PromotionServerName)."
    }

    # ----- read endpoint -------------------------------------------------------
    # The listener check above proves the WRITE server moved; it says nothing about the separate
    # read replica every site is about to be repointed at. Confirm that database is Online first,
    # so a replica that is still seeding or regionally impaired is caught before 23 sites address
    # it. An ARM read, so it also runs under -WhatIf.
    Write-DeploySection -Name 'Read replica'

    $replicaServerName = $context.TargetReplicaServerName
    $replicaResourceGroupName = $context.PromotionResourceGroupName
    $replicaDatabaseName = $context.ReplicaDatabaseName

    $replicaDatabase = Invoke-WithRetry `
        -Operation {
            Get-AzSqlDatabase -ResourceGroupName $replicaResourceGroupName `
                -ServerName $replicaServerName -DatabaseName $replicaDatabaseName -ErrorAction Stop
        } `
        -OperationName "Read the status of $replicaServerName/$replicaDatabaseName"

    if ($replicaDatabase.Status -ne 'Online') {
        throw "HALTED: read replica '$replicaDatabaseName' on '$replicaServerName' reports status '$($replicaDatabase.Status)', not 'Online'. Repointing now would send every site's read traffic to an unavailable server."
    }

    Write-DeployLog -Level Success -Message "Read replica $replicaServerName/$replicaDatabaseName is Online."

    Write-DeploySection -Name 'Read connection-string secret'
    Write-DeployLog -Level Info -Message "Read endpoint server: $($context.TargetReplicaServerName)"

    $readConnectionString = "Server=tcp:$($context.TargetReplicaServerName)$($context.SqlHostSuffix),1433;Initial Catalog=$($context.ReplicaDatabaseName);Authentication=Active Directory Default;Connection Timeout=90;"

    $versionedSecretUri = Set-ReadConnectionSecretVersion -KeyVaultName $context.KeyVaultName -SecretName $context.ReadSecretName `
        -SecretValue $readConnectionString -WhatIf:$WhatIfPreference
    $secretVersion = Split-Path -Path $versionedSecretUri -Leaf

    Write-DeployLog -Level Info -Message "Secret '$($context.ReadSecretName)' version: $secretVersion (value not logged)"

    Write-DeploySection -Name 'Read app settings'
    Write-DeployLog -Level Info -Message "Compute resource group: $($context.ComputeResourceGroupName)"

    $sites = Get-ReadEndpointSite -ComputeResourceGroupName $context.ComputeResourceGroupName
    Write-DeployLog -Level Info -Message "Sites carrying a read connection-string setting: $($sites.Count)"

    $repointedSites = @()
    foreach ($site in $sites) {
        $resolvedVersion = Update-ReadEndpointSetting -Site $site -ComputeResourceGroupName $context.ComputeResourceGroupName `
            -VersionedSecretUri $versionedSecretUri -WhatIf:$WhatIfPreference

        if ($null -eq $resolvedVersion) { $resolvedVersion = "$secretVersion (WhatIf)" }
        elseif ($resolvedVersion -ne $secretVersion) {
            Write-DeployLog -Level Warn -Message "$($site.Name) resolved secret version '$resolvedVersion' rather than '$secretVersion'."
        }

        Write-DeployLog -Level Info -Message "$($site.Name): $($site.ReadSettingName) -> version $resolvedVersion"

        $repointedSites += [pscustomobject]@{
            Name                  = $site.Name
            ReadSettingName       = $site.ReadSettingName
            ResolvedSecretVersion = $resolvedVersion
        }
    }

    Write-ConfigurationDriftReport -ActiveRegionRole $TargetRegion -ReplicaServerName $context.TargetReplicaServerName `
        -SecretName $context.ReadSecretName -SecretVersion $secretVersion -Sites $repointedSites

    # Forced promotions only. GMM's Stop already resets InProgress to Idle
    # (OperationsBackgroundService -> BulkResetJobStatusAsync), but that reset commits on the OLD
    # primary moments before a forced promotion, so it is one of the writes the promotion can
    # discard. Only Status is touched, matching BulkResetJobStatusAsync; RunId and
    # LastSuccessfulStartTime are left so the previous run stays traceable.
    $resetCount = $null
    if ($Force) {
        Write-DeploySection -Name 'Stale in-progress claims'
        $resetQuery = @"
UPDATE SyncJobs SET Status = 'Idle' WHERE Status = 'InProgress';
SELECT @@ROWCOUNT AS ResetCount;
"@

        if (-not (Get-Command -Name 'Invoke-Sqlcmd' -ErrorAction SilentlyContinue)) {
            Write-DeployLog -Level Warn -Message 'The SqlServer module is not available, so stale in-progress claims could not be reset.'
            Write-DeployLog -Level Warn -Message "Run the following against $($context.ListenerHostName) / $($context.JobsDatabaseName) before GMM is restarted:"
            Write-DeployLog -Level Warn -Message $resetQuery
        }
        elseif ($PSCmdlet.ShouldProcess("$($context.ListenerHostName) / $($context.JobsDatabaseName)", "reset SyncJobs stuck in InProgress to Idle")) {
            # Best effort. The promotion has already happened, so failing the tier here would skip
            # the restart and leave GMM down over a cleanup step.
            try {
                # Addressed through the listener so this lands on whichever server now holds the
                # read-write role.
                $resetRow = Invoke-SqlQueryWithFirewallRetry `
                    -ServerHostName $context.ListenerHostName `
                    -ServerName $context.PromotionServerName `
                    -ResourceGroupName $context.PromotionResourceGroupName `
                    -DatabaseName $context.JobsDatabaseName `
                    -Query $resetQuery | Select-Object -First 1

                $resetCount = if ($resetRow -and $null -ne $resetRow.ResetCount) { [int]$resetRow.ResetCount } else { 0 }
                Write-DeployLog -Level Success -Message "Reset $resetCount job(s) from InProgress to Idle; they will be picked up on the first trigger cycle after GMM restarts."
            }
            catch {
                Write-DeployLog -Level Warn -Message "Stale in-progress claims could not be reset ($($_.Exception.Message)). GMM will still recover them once each job's period has elapsed."
            }
        }
        else {
            Write-DeployLog -Level Info -Message 'WhatIf: SyncJobs left in InProgress would be reset to Idle before GMM restarts.'
        }
    }

    Write-DeployPhase -Name 'Step 2 - Database failover' -Event End

    $state = @{
        ActiveRegion   = $TargetRegion
        PrimaryServer  = $context.PromotionServerName
        ReplicaServer  = $context.TargetReplicaServerName
        SecretVersion  = $secretVersion
        SitesRepointed = $repointedSites.Count
    }
    if ($null -ne $dataGoodThrough) { $state['DataGoodThroughUtc'] = $dataGoodThrough }
    if ($null -ne $lagSeconds) { $state['ReplicationLagSeconds'] = $lagSeconds }
    if ($null -ne $resetCount) { $state['JobsResetToIdle'] = $resetCount }

    return New-StepResult -Step $stepName -State $state `
        -Detail "Promoted $($context.PromotionServerName); $($repointedSites.Count) site(s) repointed$(if ($null -ne $resetCount) { "; $resetCount job(s) reset to Idle" })."
}
catch {
    return New-StepResult -Step $stepName -Ok $false -Blockers @($_.Exception.Message) -Detail 'Database failover failed.'
}
