# Copyright (c) Microsoft Corporation.
# Licensed under the MIT license.
#
# Opt-in SQL integration checks for the SQL identity-types migration.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [switch]$Run,

    [Parameter(Mandatory = $false)]
    [string]$ConnectionString = $env:GMM_TEST_SQL_CONNECTION_STRING
)

$ErrorActionPreference = 'Stop'

if (-not $Run) {
    Write-Host 'SQL identity migration integration checks were not requested. Use -Run with a disposable SQL connection.'
    exit 0
}

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    throw 'An explicit integration run requires -ConnectionString or GMM_TEST_SQL_CONNECTION_STRING.'
}

$script:Passed = 0
$script:TargetConnectionString = ''
$script:StoppedStatusId = [Guid]'6c8ab321-d03f-ef11-86c3-6045bdc8336c'
$script:RunningStatusId = [Guid]'6b8ab321-d03f-ef11-86c3-6045bdc8336c'
$script:UnknownStatusId = [Guid]::NewGuid()
$script:NullStatusId = [Guid]::NewGuid()

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [AllowNull()]
        $Expected,

        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [AllowNull()]
        $Actual,

        [Parameter(Mandatory = $true)]
        [string]$Because
    )

    if ($Expected -is [string] -and $Actual -is [string]) {
        $same = [string]::Equals($Expected, $Actual, [StringComparison]::Ordinal)
    }
    else {
        $same = $Expected -eq $Actual
    }

    if (-not $same) {
        throw "Assertion failed: $Because. Expected '$Expected', actual '$Actual'."
    }

    Write-Verbose "PASS: $Because"
    $script:Passed++
}

function Assert-True {
    param(
        [Parameter(Mandatory = $true)]
        [bool]$Condition,

        [Parameter(Mandatory = $true)]
        [string]$Because
    )

    if (-not $Condition) {
        throw "Assertion failed: $Because."
    }

    Write-Verbose "PASS: $Because"
    $script:Passed++
}

function Invoke-TestNonQuery {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CommandText,

        [Parameter(Mandatory = $false)]
        [hashtable]$Parameters = @{}
    )

    $connection = [Data.SqlClient.SqlConnection]::new($script:TargetConnectionString)
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = $CommandText

        foreach ($name in $Parameters.Keys) {
            $parameter = $command.Parameters.AddWithValue($name, $Parameters[$name])
            if ($Parameters[$name] -is [string]) {
                $parameter.SqlDbType = [Data.SqlDbType]::NVarChar
                $parameter.Size = -1
            }
        }

        try {
            return $command.ExecuteNonQuery()
        }
        finally {
            $command.Dispose()
        }
    }
    finally {
        $connection.Dispose()
    }
}

function Invoke-TestScalar {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CommandText,

        [Parameter(Mandatory = $false)]
        [hashtable]$Parameters = @{}
    )

    $connection = [Data.SqlClient.SqlConnection]::new($script:TargetConnectionString)
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = $CommandText

        foreach ($name in $Parameters.Keys) {
            $parameter = $command.Parameters.AddWithValue($name, $Parameters[$name])
            if ($Parameters[$name] -is [string]) {
                $parameter.SqlDbType = [Data.SqlDbType]::NVarChar
                $parameter.Size = -1
            }
        }

        try {
            return $command.ExecuteScalar()
        }
        finally {
            $command.Dispose()
        }
    }
    finally {
        $connection.Dispose()
    }
}

function Reset-TestFixture {
    Invoke-TestNonQuery -CommandText @'
DELETE FROM dbo.ServiceStatusHistory;
DELETE FROM dbo.SyncJobs;
'@ | Out-Null
}

function Set-TestServiceStatus {
    param(
        [Parameter(Mandatory = $true)]
        [Guid]$StatusId
    )

    Invoke-TestNonQuery `
        -CommandText 'INSERT INTO dbo.ServiceStatusHistory (Id, ServiceStatusId, Timestamp) VALUES (@Id, @StatusId, SYSUTCDATETIME());' `
        -Parameters @{
            '@Id' = [Guid]::NewGuid()
            '@StatusId' = $StatusId
        } | Out-Null
}

function Add-TestJob {
    param(
        [Parameter(Mandatory = $true)]
        [Guid]$Id,

        [Parameter(Mandatory = $true)]
        [string]$Query,

        [Parameter(Mandatory = $false)]
        [string]$Marker = 'unchanged'
    )

    Invoke-TestNonQuery `
        -CommandText 'INSERT INTO dbo.SyncJobs (Id, [Query], Marker) VALUES (@Id, @Query, @Marker);' `
        -Parameters @{
            '@Id' = $Id
            '@Query' = $Query
            '@Marker' = $Marker
        } | Out-Null
}

function Get-TestJobQuery {
    param(
        [Parameter(Mandatory = $true)]
        [Guid]$Id
    )

    return [string](Invoke-TestScalar `
        -CommandText 'SELECT [Query] FROM dbo.SyncJobs WHERE Id = @Id;' `
        -Parameters @{ '@Id' = $Id })
}

function Get-TestJobMarker {
    param(
        [Parameter(Mandatory = $true)]
        [Guid]$Id
    )

    return [string](Invoke-TestScalar `
        -CommandText 'SELECT Marker FROM dbo.SyncJobs WHERE Id = @Id;' `
        -Parameters @{ '@Id' = $Id })
}

function Set-TestJobQuery {
    param(
        [Parameter(Mandatory = $true)]
        [Guid]$Id,

        [Parameter(Mandatory = $true)]
        [string]$Query
    )

    Invoke-TestNonQuery `
        -CommandText 'UPDATE dbo.SyncJobs SET [Query] = @Query WHERE Id = @Id;' `
        -Parameters @{
            '@Id' = $Id
            '@Query' = $Query
        } | Out-Null
}

function Invoke-TestMigration {
    param(
        [Parameter(Mandatory = $false)]
        [switch]$Apply
    )

    $parameters = @{
        EnvironmentAbbreviation = 'test'
        SolutionAbbreviation = 'gmm'
        ConnectionString = 'unused-by-integration-factory'
    }

    if ($Apply) {
        return Set-SqlIdentityTypesMigration @parameters -Apply -Confirm:$false
    }

    return Set-SqlIdentityTypesMigration @parameters
}

function Invoke-TestUndo {
    param(
        [Parameter(Mandatory = $false)]
        [switch]$Apply
    )

    $parameters = @{
        EnvironmentAbbreviation = 'test'
        SolutionAbbreviation = 'gmm'
        ConnectionString = 'unused-by-integration-factory'
    }

    if ($Apply) {
        return Undo-SqlIdentityTypesMigration @parameters -Apply -Confirm:$false
    }

    return Undo-SqlIdentityTypesMigration @parameters
}

function Set-TestOnlyServiceStatus {
    param(
        [Parameter(Mandatory = $true)]
        [Guid]$StatusId
    )

    Invoke-TestNonQuery -CommandText 'DELETE FROM dbo.ServiceStatusHistory;' | Out-Null
    Set-TestServiceStatus -StatusId $StatusId
}

$masterBuilder = [Data.SqlClient.SqlConnectionStringBuilder]::new($ConnectionString)
$masterBuilder['Initial Catalog'] = 'master'
$databaseName = 'GmmSqlIdentityMigration_' + [Guid]::NewGuid().ToString('N')
$databaseCreated = $false

try {
    $masterConnection = [Data.SqlClient.SqlConnection]::new($masterBuilder.ConnectionString)
    try {
        $masterConnection.Open()
        $createDatabase = $masterConnection.CreateCommand()
        $createDatabase.CommandText = "CREATE DATABASE [$databaseName];"
        try {
            [void]$createDatabase.ExecuteNonQuery()
            $databaseCreated = $true
        }
        finally {
            $createDatabase.Dispose()
        }
    }
    finally {
        $masterConnection.Dispose()
    }

    $targetBuilder = [Data.SqlClient.SqlConnectionStringBuilder]::new($ConnectionString)
    $targetBuilder['Initial Catalog'] = $databaseName
    $script:TargetConnectionString = $targetBuilder.ConnectionString

    Invoke-TestNonQuery -CommandText @'
CREATE TABLE dbo.SyncJobs
(
    Id uniqueidentifier NOT NULL PRIMARY KEY,
    [Query] nvarchar(max) NULL,
    Marker nvarchar(100) NOT NULL
);

CREATE TABLE dbo.ServiceStatuses
(
    Id uniqueidentifier NOT NULL PRIMARY KEY,
    Name nvarchar(255) NULL
);

CREATE TABLE dbo.ServiceStatusHistory
(
    Id uniqueidentifier NOT NULL PRIMARY KEY,
    ServiceStatusId uniqueidentifier NOT NULL,
    [Timestamp] datetime2 NOT NULL
);
'@ | Out-Null

    Invoke-TestNonQuery `
        -CommandText @'
INSERT INTO dbo.ServiceStatuses (Id, Name)
VALUES (@StoppedId, N'Stopped'),
       (@RunningId, N'Running'),
       (@UnknownId, N'Paused'),
       (@NullId, NULL);
'@ `
        -Parameters @{
            '@StoppedId' = $script:StoppedStatusId
            '@RunningId' = $script:RunningStatusId
            '@UnknownId' = $script:UnknownStatusId
            '@NullId' = $script:NullStatusId
        } | Out-Null

    $scriptsRoot = Split-Path $PSScriptRoot -Parent
    . (Join-Path $scriptsRoot 'ReusableModules\Invoke-SqlOperationWithFirewallRetry.ps1')
    . (Join-Path $scriptsRoot 'PostDeploymentMigrations\Set-SqlIdentityTypesMigration.ps1')
    . (Join-Path $scriptsRoot 'PostDeploymentMigrations\Undo-SqlIdentityTypesMigration.ps1')
    . (Join-Path $scriptsRoot 'PostDeploymentMigrations\Set-UpdateSourceQuery.ps1')

    function New-SqlIdentityTypesMigrationConnection {
        param([string]$ConnectionString)
        return [Data.SqlClient.SqlConnection]::new($script:TargetConnectionString)
    }

    function New-SourceQueryMigrationConnection {
        param([string]$ConnectionString)
        return [Data.SqlClient.SqlConnection]::new($script:TargetConnectionString)
    }

    $legacyQuery = '[{"type":"SqlMembership","source":{"manager":{"id":2066}}}]'
    $migratedQuery = '[{"type":"SqlMembership","source":{"manager":{"id":2066}},"identityTypes":["users"]}]'

    Reset-TestFixture
    $previewJob = [Guid]::NewGuid()
    Add-TestJob -Id $previewJob -Query $legacyQuery
    Set-TestServiceStatus -StatusId $script:RunningStatusId
    $preview = Invoke-TestMigration
    Assert-Equal 'Ready' $preview.Status 'Preview reports pending work without requiring Stopped'
    Assert-Equal $legacyQuery (Get-TestJobQuery -Id $previewJob) 'Preview writes nothing'

    Reset-TestFixture
    $validJob = [Guid]::NewGuid()
    $invalidJob = [Guid]::NewGuid()
    $invalidQuery = '[{"type":"SqlMembership","source":{"filter":"A = 1"},"identityTypes":[]}]'
    Add-TestJob -Id $validJob -Query $legacyQuery
    Add-TestJob -Id $invalidJob -Query $invalidQuery
    Set-TestServiceStatus -StatusId $script:StoppedStatusId
    $skippedBatch = Invoke-TestMigration -Apply 3>$null
    Assert-Equal 'Applied' $skippedBatch.Status 'an unreadable row does not block the rest of the batch'
    Assert-Equal 1 $skippedBatch.JobsChanged 'only the readable row is changed'
    Assert-Equal 1 $skippedBatch.JobsSkipped 'the unreadable row is counted as skipped'
    Assert-Equal $migratedQuery (Get-TestJobQuery -Id $validJob) 'the readable neighboring row is migrated'
    Assert-Equal $invalidQuery (Get-TestJobQuery -Id $invalidJob) 'the unreadable row is left unchanged'

    Reset-TestFixture
    $textJob = [Guid]::NewGuid()
    $textQuery = '[{"type":"SqlMembership","source":{"filter":"Title = \"' + [char]0x00ED + [char]::ConvertFromUtf32(0x1F600) + '\""}}]'
    $migratedTextQuery = $textQuery.Substring(0, $textQuery.Length - 2) + ',"identityTypes":["users"]}]'
    Add-TestJob -Id $textJob -Query $textQuery
    Set-TestServiceStatus -StatusId $script:StoppedStatusId
    $textResult = Invoke-TestMigration -Apply
    Assert-Equal 'Applied' $textResult.Status 'a query with non-ASCII text, an emoji and escaped quotes is migrated'
    Assert-Equal $migratedTextQuery (Get-TestJobQuery -Id $textJob) 'non-ASCII text, an emoji and escaped quotes survive the database round trip'

    Reset-TestFixture
    $preservedJob = [Guid]::NewGuid()
    $convertedJob = [Guid]::NewGuid()
    $preservedQuery = '[{"type":"SqlMembership","source":{"filter":"users.EmployeeType_Code != ''Vendor''"},"identityTypes":["users"],"future":{"version":2}}]'
    $legacyChainQuery = '[{"type":"SqlMembership","source":{"filter":"EmployeeType_Code != ''Vendor''"}}]'
    $convertedLegacyChainQuery =
        (Convert-SqlSourceQuery -Query $legacyChainQuery).TransformedQuery
    $migratedLegacyChainQuery =
        (Convert-SqlIdentityTypesQuery -Query $convertedLegacyChainQuery).TransformedQuery
    Add-TestJob -Id $preservedJob -Query $preservedQuery
    Add-TestJob -Id $convertedJob -Query $legacyChainQuery

    Set-UpdateSourceQuery `
        -EnvironmentAbbreviation 'test' `
        -SolutionAbbreviation 'gmm' `
        -ConnectionString 'unused'

    Assert-Equal $preservedQuery `
        (Get-TestJobQuery -Id $preservedJob) `
        'the database converter preserves a current explicit SQL part byte-for-byte'
    Assert-Equal $convertedLegacyChainQuery `
        (Get-TestJobQuery -Id $convertedJob) `
        'the database converter retains its legacy SQL rewrite'

    Set-TestServiceStatus -StatusId $script:StoppedStatusId
    $orderedChain = Invoke-TestMigration -Apply
    Assert-Equal 'Applied' $orderedChain.Status 'the identity migration applies after the converter'
    Assert-Equal $preservedQuery `
        (Get-TestJobQuery -Id $preservedJob) `
        'the ordered chain preserves the current explicit SQL part'
    Assert-Equal $migratedLegacyChainQuery `
        (Get-TestJobQuery -Id $convertedJob) `
        'the ordered chain adds users-only selection after legacy conversion'

    Set-UpdateSourceQuery `
        -EnvironmentAbbreviation 'test' `
        -SolutionAbbreviation 'gmm' `
        -ConnectionString 'unused'
    $secondDeployment = Invoke-TestMigration -Apply
    Assert-Equal 'NoChanges' $secondDeployment.Status 'a second ordered deployment is idempotent'
    Assert-Equal $preservedQuery `
        (Get-TestJobQuery -Id $preservedJob) `
        'a second deployment still preserves the current explicit SQL part'
    Assert-Equal $migratedLegacyChainQuery `
        (Get-TestJobQuery -Id $convertedJob) `
        'a second deployment cannot remove the migrated users-only selection'

    foreach ($statusCase in @(
        @{ Name = 'Running'; StatusId = $script:RunningStatusId },
        @{ Name = 'unknown'; StatusId = $script:UnknownStatusId },
        @{ Name = 'null'; StatusId = $script:NullStatusId }
    )) {
        Reset-TestFixture
        $jobId = [Guid]::NewGuid()
        Add-TestJob -Id $jobId -Query $legacyQuery
        Set-TestServiceStatus -StatusId $statusCase.StatusId
        $result = Invoke-TestMigration -Apply
        Assert-Equal 'Rejected' $result.Status "$($statusCase.Name) service status rejects Apply"
        Assert-Equal 'ServiceNotStopped' $result.ReasonCode "$($statusCase.Name) uses the bounded status reason"
        Assert-Equal $legacyQuery (Get-TestJobQuery -Id $jobId) "$($statusCase.Name) status produces no mutation"
    }

    Reset-TestFixture
    $missingHistoryJob = [Guid]::NewGuid()
    Add-TestJob -Id $missingHistoryJob -Query $legacyQuery
    $missingHistory = Invoke-TestMigration -Apply
    Assert-Equal 'Rejected' $missingHistory.Status 'missing service history rejects Apply'
    Assert-Equal $legacyQuery (Get-TestJobQuery -Id $missingHistoryJob) 'missing history produces no mutation'

    Reset-TestFixture
    $unmappedStatusJob = [Guid]::NewGuid()
    Add-TestJob -Id $unmappedStatusJob -Query $legacyQuery
    Invoke-TestNonQuery `
        -CommandText @'
INSERT INTO dbo.ServiceStatusHistory (Id, ServiceStatusId, Timestamp)
VALUES (@OlderId, @StoppedId, DATEADD(minute, -1, SYSUTCDATETIME())),
       (@NewestId, @UnmappedId, SYSUTCDATETIME());
'@ `
        -Parameters @{
            '@OlderId' = [Guid]::NewGuid()
            '@StoppedId' = $script:StoppedStatusId
            '@NewestId' = [Guid]::NewGuid()
            '@UnmappedId' = [Guid]::NewGuid()
        } | Out-Null

    $unmappedStatus = Invoke-TestMigration -Apply
    Assert-Equal 'Rejected' $unmappedStatus.Status 'an unmapped newest service status rejects Apply'
    Assert-Equal 'ServiceNotStopped' $unmappedStatus.ReasonCode 'an unmapped newest status uses the bounded status reason'
    Assert-Equal $legacyQuery (Get-TestJobQuery -Id $unmappedStatusJob) 'an older Stopped row cannot bypass an unmapped newest status'

    Reset-TestFixture
    $applyJob = [Guid]::NewGuid()
    $explicitAgentJob = [Guid]::NewGuid()
    $groupJob = [Guid]::NewGuid()
    Add-TestJob -Id $applyJob -Query $legacyQuery -Marker 'retain-me'
    Add-TestJob -Id $explicitAgentJob -Query '[{"type":"SqlMembership","source":{"filter":"agents.BlueprintId = ''alpha''"},"identityTypes":["agents"]}]'
    Add-TestJob -Id $groupJob -Query '[{"type":"GroupMembership","source":"11111111-1111-1111-1111-111111111111"}]'
    Set-TestServiceStatus -StatusId $script:StoppedStatusId
    $applied = Invoke-TestMigration -Apply
    Assert-Equal 'Applied' $applied.Status 'Stopped Apply succeeds'
    Assert-Equal 1 $applied.JobsChanged 'only the missing SQL selection is changed'
    Assert-Equal $migratedQuery (Get-TestJobQuery -Id $applyJob) 'Apply writes the exact transformed query'
    Assert-Equal 'retain-me' (Get-TestJobMarker -Id $applyJob) 'Apply changes only Query'
    Assert-Equal '[{"type":"SqlMembership","source":{"filter":"agents.BlueprintId = ''alpha''"},"identityTypes":["agents"]}]' `
        (Get-TestJobQuery -Id $explicitAgentJob) `
        'Apply preserves an explicit agents selection'
    Assert-Equal '[{"type":"GroupMembership","source":"11111111-1111-1111-1111-111111111111"}]' `
        (Get-TestJobQuery -Id $groupJob) `
        'Apply preserves non-SQL queries'

    $rerun = Invoke-TestMigration -Apply
    Assert-Equal 'NoChanges' $rerun.Status 'an ordinary later rerun is NoChanges'

    Reset-TestFixture
    Set-TestServiceStatus -StatusId $script:StoppedStatusId
    $emptyDatabase = Invoke-TestMigration -Apply
    Assert-Equal 'NoChanges' $emptyDatabase.Status 'an empty operational batch is NoChanges'

    Reset-TestFixture
    $rollbackFirst = [Guid]::NewGuid()
    $rollbackSecond = [Guid]::NewGuid()
    $secondBefore = '[{"type":"SqlMembership","source":{"manager":{"id":1002}}}]'
    $secondConflict = '[{"type":"SqlMembership","source":{"manager":{"id":2002}}}]'
    Add-TestJob -Id $rollbackFirst -Query $legacyQuery
    Add-TestJob -Id $rollbackSecond -Query $secondBefore
    Set-TestServiceStatus -StatusId $script:StoppedStatusId
    $rollbackPlan = Get-SqlIdentityTypesMigrationPlan -Rows @(
        [PSCustomObject]@{ Id = $rollbackFirst; Query = $legacyQuery },
        [PSCustomObject]@{ Id = $rollbackSecond; Query = $secondBefore }
    )
    Set-TestJobQuery -Id $rollbackSecond -Query $secondConflict
    $transactionConnection = New-SqlIdentityTypesMigrationConnection -ConnectionString 'unused'
    try {
        $transactionResult = Invoke-SqlIdentityTypesMigrationTransaction `
            -Connection $transactionConnection `
            -Changes $rollbackPlan.Changes
    }
    finally {
        $transactionConnection.Dispose()
    }

    Assert-Equal 'CompareAndSwapMiss' $transactionResult.Outcome 'one stale row causes a compare-and-swap miss'
    Assert-Equal $legacyQuery (Get-TestJobQuery -Id $rollbackFirst) 'the transaction rolls back a neighboring successful update'
    Assert-Equal $secondConflict (Get-TestJobQuery -Id $rollbackSecond) 'the newer conflicting value is not overwritten'

    Reset-TestFixture
    $concurrentFirst = [Guid]::NewGuid()
    $concurrentSecond = [Guid]::NewGuid()
    Add-TestJob -Id $concurrentFirst -Query $legacyQuery
    Add-TestJob -Id $concurrentSecond -Query $secondBefore
    Set-TestServiceStatus -StatusId $script:StoppedStatusId
    $script:ConcurrentJobId = $concurrentSecond
    $script:SavedTransaction = ${function:Invoke-SqlIdentityTypesMigrationTransaction}

    # Simulates another writer changing a row between the plan being read and each update attempt.
    $concurrentWriterTransaction = {
        param(
            [Data.SqlClient.SqlConnection]$Connection,
            [object[]]$Changes
        )

        if ($script:TransactionCalls -lt $script:ConcurrentQueries.Count) {
            Set-TestJobQuery -Id $script:ConcurrentJobId -Query $script:ConcurrentQueries[$script:TransactionCalls]
        }

        $script:TransactionCalls++
        return & $script:SavedTransaction -Connection $Connection -Changes $Changes
    }

    $script:ConcurrentQueries = @($secondConflict)
    $script:TransactionCalls = 0
    Set-Item -LiteralPath 'function:Invoke-SqlIdentityTypesMigrationTransaction' -Value $concurrentWriterTransaction
    try {
        $retriedResult = Invoke-TestMigration -Apply -WarningVariable retryWarnings 3>$null
    }
    finally {
        Set-Item -LiteralPath 'function:Invoke-SqlIdentityTypesMigrationTransaction' -Value $script:SavedTransaction
    }

    $secondConflictMigrated = $secondConflict.Substring(0, $secondConflict.Length - 2) + ',"identityTypes":["users"]}]'
    Assert-Equal 2 $script:TransactionCalls 'one concurrent change leads to one retry'
    Assert-True (@($retryWarnings | Where-Object { $_.Message.Contains('retrying once') }).Count -eq 1) 'the retry is announced once'
    Assert-Equal 'Applied' $retriedResult.Status 'the retry applies the migration after one concurrent change'
    Assert-Equal 2 $retriedResult.JobsChanged 'the retry changes both jobs'
    Assert-Equal $migratedQuery (Get-TestJobQuery -Id $concurrentFirst) 'the retry migrates the unchanged job'
    Assert-Equal $secondConflictMigrated (Get-TestJobQuery -Id $concurrentSecond) 'the retry migrates the newer value of the changed job'

    Reset-TestFixture
    Add-TestJob -Id $concurrentFirst -Query $legacyQuery
    Add-TestJob -Id $concurrentSecond -Query $secondBefore
    Set-TestServiceStatus -StatusId $script:StoppedStatusId
    $secondRepeatConflict = '[{"type":"SqlMembership","source":{"manager":{"id":3003}}}]'
    $script:ConcurrentQueries = @($secondConflict, $secondRepeatConflict)
    $script:TransactionCalls = 0
    Set-Item -LiteralPath 'function:Invoke-SqlIdentityTypesMigrationTransaction' -Value $concurrentWriterTransaction
    try {
        $concurrentResult = Invoke-TestMigration -Apply 3>$null
    }
    finally {
        Set-Item -LiteralPath 'function:Invoke-SqlIdentityTypesMigrationTransaction' -Value $script:SavedTransaction
    }

    Assert-Equal 2 $script:TransactionCalls 'a second concurrent change is not retried again'
    Assert-Equal 'Conflict' $concurrentResult.Status 'a row changed during both attempts is reported as a conflict'
    Assert-Equal 'ConcurrentQueryChange' $concurrentResult.ReasonCode 'a conflict reports the concurrent change'
    Assert-Equal 0 $concurrentResult.JobsChanged 'a conflict reports no changed jobs'
    Assert-Equal $legacyQuery (Get-TestJobQuery -Id $concurrentFirst) 'a conflict rolls back the whole batch'
    Assert-Equal $secondRepeatConflict (Get-TestJobQuery -Id $concurrentSecond) 'a conflict does not overwrite the concurrent change'

    foreach ($conflictCase in @(
        @{ Name = 'equal-length different contents'; Current = '[{"type":"SqlMembership","source":{"manager":{"id":3066}}}]' },
        @{ Name = 'case-only difference'; Current = '[{"type":"SQLMembership","source":{"manager":{"id":2066}}}]' },
        @{ Name = 'trailing-space difference'; Current = $legacyQuery + ' ' }
    )) {
        Reset-TestFixture
        $jobId = [Guid]::NewGuid()
        Add-TestJob -Id $jobId -Query $legacyQuery
        Set-TestServiceStatus -StatusId $script:StoppedStatusId
        $stalePlan = Get-SqlIdentityTypesMigrationPlan -Rows @(
            [PSCustomObject]@{ Id = $jobId; Query = $legacyQuery }
        )
        Set-TestJobQuery -Id $jobId -Query $conflictCase.Current

        $conflictConnection = New-SqlIdentityTypesMigrationConnection -ConnectionString 'unused'
        try {
            $conflictResult = Invoke-SqlIdentityTypesMigrationTransaction `
                -Connection $conflictConnection `
                -Changes $stalePlan.Changes
        }
        finally {
            $conflictConnection.Dispose()
        }

        Assert-Equal 'CompareAndSwapMiss' $conflictResult.Outcome "$($conflictCase.Name) fails exact compare-and-swap"
        Assert-Equal $conflictCase.Current (Get-TestJobQuery -Id $jobId) "$($conflictCase.Name) is not overwritten"

        $stateConnection = New-SqlIdentityTypesMigrationConnection -ConnectionString 'unused'
        try {
            $states = Get-SqlIdentityTypesMigrationStates -Connection $stateConnection -Changes $stalePlan.Changes
        }
        finally {
            $stateConnection.Dispose()
        }

        Assert-Equal 'Conflict' `
            (Resolve-SqlIdentityTypesMigrationStates -States $states -ExpectedCount 1) `
            "$($conflictCase.Name) classifies as Conflict"
    }

    Reset-TestFixture
    $alreadyAppliedJob = [Guid]::NewGuid()
    Add-TestJob -Id $alreadyAppliedJob -Query $legacyQuery
    Set-TestServiceStatus -StatusId $script:StoppedStatusId
    $alreadyAppliedPlan = Get-SqlIdentityTypesMigrationPlan -Rows @(
        [PSCustomObject]@{ Id = $alreadyAppliedJob; Query = $legacyQuery }
    )
    Set-TestJobQuery -Id $alreadyAppliedJob -Query $migratedQuery

    $alreadyAppliedConnection = New-SqlIdentityTypesMigrationConnection -ConnectionString 'unused'
    try {
        $alreadyAppliedTransaction = Invoke-SqlIdentityTypesMigrationTransaction `
            -Connection $alreadyAppliedConnection `
            -Changes $alreadyAppliedPlan.Changes
    }
    finally {
        $alreadyAppliedConnection.Dispose()
    }

    Assert-Equal 'CompareAndSwapMiss' $alreadyAppliedTransaction.Outcome 'a concurrent exact after-value misses the before-value guard'

    $alreadyAppliedStateConnection = New-SqlIdentityTypesMigrationConnection -ConnectionString 'unused'
    try {
        $alreadyAppliedStates = Get-SqlIdentityTypesMigrationStates `
            -Connection $alreadyAppliedStateConnection `
            -Changes $alreadyAppliedPlan.Changes
    }
    finally {
        $alreadyAppliedStateConnection.Dispose()
    }

    Assert-Equal 'After' `
        (Resolve-SqlIdentityTypesMigrationStates -States $alreadyAppliedStates -ExpectedCount 1) `
        'exact read-back supports AlreadyApplied and ambiguous-commit classification'

    Reset-TestFixture
    $undoLegacyJob = [Guid]::NewGuid()
    $undoMixedJob = [Guid]::NewGuid()
    $undoKeptJob = [Guid]::NewGuid()
    $undoInvalidJob = [Guid]::NewGuid()
    $undoMixedQuery = '[{"type":"GroupMembership","source":"11111111-1111-1111-1111-111111111111"},' +
        '{"type":"SqlMembership","source":{"filter":"Title = \"' + [char]0x00ED + [char]::ConvertFromUtf32(0x1F600) + '\""}},' +
        '{"type":"SqlMembership","source":{"manager":{"id":5}},"identityTypes":["agents"]}]'
    $undoKeptQuery = '[{"type":"SqlMembership","source":{"manager":{"id":6}},"identityTypes":["users","agents"]}]'
    Add-TestJob -Id $undoLegacyJob -Query $legacyQuery -Marker 'retain-me'
    Add-TestJob -Id $undoMixedJob -Query $undoMixedQuery
    Add-TestJob -Id $undoKeptJob -Query $undoKeptQuery
    Set-TestOnlyServiceStatus -StatusId $script:StoppedStatusId
    $undoSetup = Invoke-TestMigration -Apply
    Assert-Equal 'Applied' $undoSetup.Status 'the migration applies before the undo runs'
    Assert-Equal 2 $undoSetup.JobsChanged 'the migration adds the users-only selection to the two jobs without one'
    $migratedMixedQuery = Get-TestJobQuery -Id $undoMixedJob
    Add-TestJob -Id $undoInvalidJob -Query $invalidQuery

    Set-TestOnlyServiceStatus -StatusId $script:RunningStatusId
    $undoPreview = Invoke-TestUndo 3>$null
    Assert-Equal 'Ready' $undoPreview.Status 'an undo preview reports pending work without requiring Stopped'
    Assert-Equal 2 $undoPreview.JobsToChange 'an undo preview counts the jobs it would revert'
    Assert-Equal 2 $undoPreview.JobsWithKeptSelections 'an undo preview counts the jobs whose other selections it keeps'
    Assert-Equal 1 $undoPreview.JobsSkipped 'an undo preview counts the unreadable job'
    Assert-Equal $migratedQuery (Get-TestJobQuery -Id $undoLegacyJob) 'an undo preview writes nothing'

    $undoWhileRunning = Invoke-TestUndo -Apply 3>$null
    Assert-Equal 'Rejected' $undoWhileRunning.Status 'the undo refuses to write while the service is running'
    Assert-Equal 'ServiceNotStopped' $undoWhileRunning.ReasonCode 'the undo reports the service status reason'
    Assert-Equal $migratedQuery (Get-TestJobQuery -Id $undoLegacyJob) 'a refused undo writes nothing'
    Assert-Equal $migratedMixedQuery (Get-TestJobQuery -Id $undoMixedJob) 'a refused undo leaves every job unchanged'

    Set-TestOnlyServiceStatus -StatusId $script:StoppedStatusId
    $undone = Invoke-TestUndo -Apply 3>$null
    Assert-Equal 'Applied' $undone.Status 'the undo applies while the service is stopped'
    Assert-Equal 2 $undone.JobsChanged 'the undo reverts the two migrated jobs'
    Assert-Equal 1 $undone.JobsSkipped 'the undo skips the unreadable job'
    Assert-Equal $legacyQuery (Get-TestJobQuery -Id $undoLegacyJob) 'the undo restores the original query'
    Assert-Equal 'retain-me' (Get-TestJobMarker -Id $undoLegacyJob) 'the undo changes only Query'
    Assert-Equal $undoMixedQuery (Get-TestJobQuery -Id $undoMixedJob) 'the undo restores unusual text and keeps the explicit agents part byte-for-byte'
    Assert-Equal $undoKeptQuery (Get-TestJobQuery -Id $undoKeptJob) 'the undo keeps a selection the migration did not write'
    Assert-Equal $invalidQuery (Get-TestJobQuery -Id $undoInvalidJob) 'the undo leaves the unreadable job unchanged'

    $undoRerun = Invoke-TestUndo -Apply 3>$null
    Assert-Equal 'NoChanges' $undoRerun.Status 'a second undo has nothing to revert'

    Reset-TestFixture
    $readFailureJob = [Guid]::NewGuid()
    Add-TestJob -Id $readFailureJob -Query $legacyQuery
    Invoke-TestNonQuery -CommandText 'DROP TABLE dbo.ServiceStatusHistory;' | Out-Null
    $readFailure = Invoke-TestMigration -Apply
    Assert-Equal 'Rejected' $readFailure.Status 'a service-status read failure rejects Apply'
    Assert-Equal 'ServiceStatusReadFailed' $readFailure.ReasonCode 'status read failure uses a bounded reason'
    Assert-Equal $legacyQuery (Get-TestJobQuery -Id $readFailureJob) 'status read failure produces no mutation'

    Write-Host ("{0} SQL migration integration checks passed." -f $script:Passed) -ForegroundColor Green
}
finally {
    if ($databaseCreated) {
        [Data.SqlClient.SqlConnection]::ClearAllPools()
        $cleanupConnection = [Data.SqlClient.SqlConnection]::new($masterBuilder.ConnectionString)
        try {
            $cleanupConnection.Open()
            $cleanupCommand = $cleanupConnection.CreateCommand()
            $cleanupCommand.CommandText = "DROP DATABASE [$databaseName];"
            try {
                [void]$cleanupCommand.ExecuteNonQuery()
            }
            finally {
                $cleanupCommand.Dispose()
            }
        }
        finally {
            $cleanupConnection.Dispose()
        }
    }
}
