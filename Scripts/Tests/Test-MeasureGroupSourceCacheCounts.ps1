# Copyright (c) Microsoft Corporation.
# Licensed under the MIT license.

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$script:Passed = 0

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)]
        $Expected,

        [Parameter(Mandatory = $true)]
        $Actual,

        [Parameter(Mandatory = $true)]
        [string]$Because
    )

    if ($Expected -ne $Actual) {
        throw "Assertion failed: $Because. Expected '$Expected', actual '$Actual'."
    }

    $script:Passed++
}

function Assert-Throws {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$Action,

        [Parameter(Mandatory = $true)]
        [string]$MessagePattern,

        [Parameter(Mandatory = $true)]
        [string]$Because
    )

    try {
        & $Action
    }
    catch {
        if ($_.Exception.Message -notmatch $MessagePattern) {
            throw "Assertion failed: $Because. Unexpected error '$($_.Exception.Message)'."
        }

        $script:Passed++
        return
    }

    throw "Assertion failed: $Because. No error was thrown."
}

function New-Blob {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [object]$LastModified
    )

    return [PSCustomObject]@{
        name = $Name
        properties = [PSCustomObject]@{
            lastModified = $LastModified
        }
    }
}

. (Join-Path $PSScriptRoot 'Measure-GroupSourceCacheCounts.ps1')

$smallGroupId = [Guid]'00000000-0000-4000-8000-000000000001'
$largeGroupId = [Guid]'00000000-0000-4000-8000-000000000002'
$ignoredGroupId = [Guid]'00000000-0000-4000-8000-000000000003'
$blobs = @(
    (New-Blob `
        -Name "cache/${smallGroupId}_01012026-0100.txt" `
        -LastModified '2026-01-01T01:00:00Z'),
    (New-Blob `
        -Name "cache/${smallGroupId}_01022026-0100.txt" `
        -LastModified '2026-01-02T01:00:00Z'),
    (New-Blob `
        -Name "cache/${largeGroupId}_01012026-0100.txt" `
        -LastModified '2026-01-01T01:00:00Z'),
    (New-Blob `
        -Name "cache/delta_${ignoredGroupId}_01012026-0100.json" `
        -LastModified '2026-01-01T01:00:00Z'),
    (New-Blob `
        -Name "${ignoredGroupId}/01012026-0100_Aggregated.json" `
        -LastModified '2026-01-01T01:00:00Z')
)

$latest = @(Get-LatestGroupSourceCaches `
    -Blobs $blobs `
    -GroupIds @($smallGroupId, $largeGroupId))
Assert-Equal 2 $latest.Count 'one latest cache is returned per requested group'
Assert-Equal $smallGroupId $latest[0].GroupId 'results are ordered by source group'
Assert-Equal "cache/${smallGroupId}_01022026-0100.txt" $latest[0].Name `
    'the latest cache is selected by last-modified time'

$firstMemberId = [Guid]'10000000-0000-4000-8000-000000000001'
$secondMemberId = [Guid]'10000000-0000-4000-8000-000000000002'
$memberCount = Measure-CacheMemberLines `
    -Lines @($firstMemberId.ToString(), $secondMemberId.ToString()) `
    -BlobName 'cache/test.txt'
Assert-Equal 2 $memberCount 'cache GUID lines are counted without deserializing a membership document'
Assert-Equal 0 (Measure-CacheMemberLines -Lines @() -BlobName 'cache/empty.txt') `
    'an empty cache has zero members'

Assert-Throws `
    -Action {
        Get-LatestGroupSourceCaches `
            -Blobs @() `
            -GroupIds @([Guid]'00000000-0000-4000-8000-000000000004')
    } `
    -MessagePattern 'No membership cache was found' `
    -Because 'an empty cache inventory reaches the intended missing-cache guard'

$measurementScript = Join-Path $PSScriptRoot 'Measure-GroupSourceCacheCounts.ps1'
function global:az {
    param(
        [Parameter(ValueFromRemainingArguments = $true)]
        [object[]]$Arguments
    )

    $global:LASTEXITCODE = 0
    return '[]'
}
try {
    Assert-Throws `
        -Action {
            & $measurementScript `
                -ContainerUri 'https://account.blob.core.windows.net/membership' `
                -GroupId @([Guid]'00000000-0000-4000-8000-000000000004')
        } `
        -MessagePattern "No membership cache was found for source group '00000000-0000-4000-8000-000000000004'" `
        -Because 'the script body preserves an empty az inventory and reaches the group-specific missing-cache guard'
}
finally {
    Remove-Item Function:\global:az
}

Assert-Throws `
    -Action {
        Get-LatestGroupSourceCaches `
            -Blobs $blobs `
            -GroupIds @([Guid]'00000000-0000-4000-8000-000000000004')
    } `
    -MessagePattern 'No membership cache was found' `
    -Because 'a requested source group without a cache fails closed'

$invalidTimestampBlob = New-Blob `
    -Name "cache/${smallGroupId}_01032026-0100.txt" `
    -LastModified 'not-a-date'
Assert-Throws `
    -Action {
        Get-LatestGroupSourceCaches `
            -Blobs @($invalidTimestampBlob) `
            -GroupIds @($smallGroupId)
    } `
    -MessagePattern 'no valid last-modified timestamp' `
    -Because 'a cache without a usable ordering timestamp fails closed'

$azStyleBlob = @(
    "[{`"name`":`"cache/${smallGroupId}_01032026-0100.txt`",`"properties`":{`"lastModified`":`"2026-01-03T01:02:03.1234567+00:00`"}}]" |
        ConvertFrom-Json
)
$azStyleLatest = @(Get-LatestGroupSourceCaches `
    -Blobs $azStyleBlob `
    -GroupIds @($smallGroupId))
Assert-Equal `
    '2026-01-03T01:02:03.1234567+00:00' `
    $azStyleLatest[0].LastModifiedUtc.ToString('o') `
    'az-style JSON preserves the exact UTC cache timestamp'

Assert-Throws `
    -Action {
        Measure-CacheMemberLines `
            -Lines @($firstMemberId.ToString(), 'not-a-guid') `
            -BlobName 'cache/invalid.txt'
    } `
    -MessagePattern 'contains a line that is not a GUID' `
    -Because 'a cache that current readers cannot parse fails closed'

function global:az {
    if ($args -contains 'download') {
        $global:LASTEXITCODE = 0
        $firstMemberId.ToString()
        $secondMemberId.ToString()
        return
    }

    throw "Unexpected mocked az invocation: $args"
}

try {
    $remoteMemberCount = Measure-RemoteCacheMemberLines `
        -StorageAccountName 'account' `
        -ContainerName 'membership' `
        -BlobName 'cache/test.txt'
    Assert-Equal 2 $remoteMemberCount `
        'the remote counting path uses the shared GUID-line validator'
}
finally {
    Remove-Item Function:\global:az
}

Write-Host ("{0} group-source cache count checks passed." -f $script:Passed) -ForegroundColor Green
