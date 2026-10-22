# Copyright (c) Microsoft Corporation.
# Licensed under the MIT license.
#
# Streams the latest membership cache for each requested source group and reports
# its line count without modifying storage.

[CmdletBinding()]
param(
    [Uri]$ContainerUri,

    [Guid[]]$GroupId
)

$ErrorActionPreference = 'Stop'

function Get-LatestGroupSourceCaches {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]]$Blobs,

        [Parameter(Mandatory = $true)]
        [Guid[]]$GroupIds
    )

    $requestedGroupIds = @{}
    foreach ($requestedGroupId in $GroupIds) {
        $requestedGroupIds[$requestedGroupId.ToString('D')] = $true
    }

    $cacheNamePattern = '^cache/(?<GroupId>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})_\d{8}-\d{4}\.txt$'
    $cacheBlobs = foreach ($blob in $Blobs) {
        $name = [string]$blob.name
        $match = [regex]::Match($name, $cacheNamePattern)
        if (-not $match.Success) {
            continue
        }

        $matchedGroupId = [Guid]$match.Groups['GroupId'].Value
        if (-not $requestedGroupIds.ContainsKey($matchedGroupId.ToString('D'))) {
            continue
        }

        $lastModifiedValue = $blob.properties.lastModified
        [DateTimeOffset]$lastModified = [DateTimeOffset]::MinValue
        if ($lastModifiedValue -is [DateTimeOffset]) {
            $lastModified = [DateTimeOffset]$lastModifiedValue
        }
        elseif ($lastModifiedValue -is [DateTime]) {
            $lastModifiedDateTime = [DateTime]$lastModifiedValue
            $lastModified = if ($lastModifiedDateTime.Kind -eq [DateTimeKind]::Unspecified) {
                [DateTimeOffset]::new(
                    [DateTime]::SpecifyKind(
                        $lastModifiedDateTime,
                        [DateTimeKind]::Utc))
            }
            else {
                [DateTimeOffset]::new($lastModifiedDateTime)
            }
        }
        elseif (-not [DateTimeOffset]::TryParse(
            [string]$lastModifiedValue,
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::AssumeUniversal,
            [ref]$lastModified)) {
            throw "Cache blob '$name' has no valid last-modified timestamp."
        }

        [PSCustomObject]@{
            GroupId = $matchedGroupId
            Name = $name
            LastModifiedUtc = $lastModified.ToUniversalTime()
        }
    }

    $latestByGroupId = @{}
    foreach ($cacheBlob in $cacheBlobs) {
        $key = $cacheBlob.GroupId.ToString('D')
        if (-not $latestByGroupId.ContainsKey($key) -or
            $cacheBlob.LastModifiedUtc -gt $latestByGroupId[$key].LastModifiedUtc) {
            $latestByGroupId[$key] = $cacheBlob
        }
    }

    foreach ($requestedGroupId in $GroupIds) {
        $key = $requestedGroupId.ToString('D')
        if (-not $latestByGroupId.ContainsKey($key)) {
            throw "No membership cache was found for source group '$requestedGroupId'."
        }
    }

    return @($latestByGroupId.Values | Sort-Object GroupId)
}

function Measure-CacheMemberLines {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [string[]]$Lines,

        [Parameter(Mandatory = $true)]
        [string]$BlobName
    )

    [long]$count = 0
    foreach ($line in $Lines) {
        Assert-CacheMemberLine -Line $line -BlobName $BlobName
        $count++
    }

    return $count
}

function Assert-CacheMemberLine {
    param(
        [AllowEmptyString()]
        [string]$Line,

        [Parameter(Mandatory = $true)]
        [string]$BlobName
    )

    [Guid]$memberId = [Guid]::Empty
    if ([string]::IsNullOrWhiteSpace($Line) -or
        -not [Guid]::TryParse($Line, [ref]$memberId)) {
        throw "Cache blob '$BlobName' contains a line that is not a GUID."
    }
}

function Measure-RemoteCacheMemberLines {
    param(
        [Parameter(Mandatory = $true)]
        [string]$StorageAccountName,

        [Parameter(Mandatory = $true)]
        [string]$ContainerName,

        [Parameter(Mandatory = $true)]
        [string]$BlobName
    )

    [long]$count = 0
    & az storage blob download `
        --account-name $StorageAccountName `
        --container-name $ContainerName `
        --name $BlobName `
        --auth-mode login `
        --no-progress `
        --only-show-errors |
        ForEach-Object {
            Assert-CacheMemberLine -Line ([string]$_) -BlobName $BlobName
            $count++
        }

    if ($LASTEXITCODE -ne 0) {
        throw "Unable to stream cache blob '$BlobName'."
    }

    return $count
}

if ($MyInvocation.InvocationName -ne '.') {
    if ($null -eq $ContainerUri -or $GroupId.Count -eq 0) {
        throw 'Provide ContainerUri and at least one GroupId.'
    }

    $containerName = $ContainerUri.AbsolutePath.Trim('/')
    if ([string]::IsNullOrWhiteSpace($containerName) -or $containerName.Contains('/')) {
        throw "ContainerUri must identify one blob container, not a blob or account root."
    }

    $storageAccountName = $ContainerUri.Host.Split('.')[0]
    if ([string]::IsNullOrWhiteSpace($storageAccountName)) {
        throw "ContainerUri does not contain a storage account name."
    }

    $latestCaches = foreach ($sourceGroupId in $GroupId) {
        $inventoryJson = @(
            & az storage blob list `
                --account-name $storageAccountName `
                --container-name $containerName `
                --auth-mode login `
                --prefix "cache/$sourceGroupId" `
                --num-results '*' `
                --only-show-errors `
                --output json
        ) -join [Environment]::NewLine

        if ($LASTEXITCODE -ne 0) {
            throw "Unable to list membership cache blobs for source group '$sourceGroupId'."
        }

        $blobs = @(
            if (-not [string]::IsNullOrWhiteSpace($inventoryJson)) {
                $inventoryJson | ConvertFrom-Json
            }
        )

        Get-LatestGroupSourceCaches -Blobs $blobs -GroupIds @($sourceGroupId)
    }

    $results = foreach ($cache in $latestCaches) {
        [PSCustomObject]@{
            GroupId = $cache.GroupId
            MemberCount = Measure-RemoteCacheMemberLines `
                -StorageAccountName $storageAccountName `
                -ContainerName $containerName `
                -BlobName $cache.Name
            LastModifiedUtc = $cache.LastModifiedUtc
            CacheBlob = $cache.Name
        }
    }

    $results | Sort-Object `
        @{ Expression = 'MemberCount'; Descending = $true },
        @{ Expression = 'GroupId'; Descending = $false }
}
