# Copyright (c) Microsoft Corporation.
# Licensed under the MIT license.
#
# Dependency-free checks for the post-deployment source-query converter and the SQL
# firewall retry helper it runs through.

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$script:Passed = 0

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

    $script:Passed++
}

$scriptsRoot = Split-Path $PSScriptRoot -Parent

$firewallHelper = Join-Path $scriptsRoot 'ReusableModules\Invoke-SqlOperationWithFirewallRetry.ps1'
. $firewallHelper
function Get-AzSqlServerFirewallRule {
    return $null
}
function New-AzSqlServerFirewallRule {
    return [PSCustomObject]@{ Name = 'unexpected-success-output' }
}

$firewallOutput = @(
    Add-SqlIpFromError `
        -ErrorMessage "Client with IP address '10.20.30.40' is not allowed." `
        -SolutionAbbreviation 'gmm' `
        -EnvironmentAbbreviation 'test'
)
Assert-Equal 0 $firewallOutput.Count 'firewall-rule creation does not contaminate SQL operation results'
Remove-Item -LiteralPath 'function:Get-AzSqlServerFirewallRule'
Remove-Item -LiteralPath 'function:New-AzSqlServerFirewallRule'

$converterScript = Join-Path $scriptsRoot 'PostDeploymentMigrations\Set-UpdateSourceQuery.ps1'
. $converterScript
Assert-True ($null -ne (Get-Command Convert-SqlSourceQuery -ErrorAction SilentlyContinue)) 'the source converter exposes a pure transform'

$preservationCases = @(
    '[{"type":"SqlMembership","source":{"manager":{"id":1},"filter":"users.EmployeeType = ''FTE''"},"identityTypes":["users"]}]',
    '[{"type":"SqlMembership","source":{"filter":"agents.BlueprintId = ''alpha''"},"identityTypes":["agents"]}]',
    '[{"type":"SqlMembership","source":{"manager":{"id":1},"filter":"users.EmployeeType IN (''FTE'',''Vendor'')"},"identityTypes":["users","agents"]}]',
    '[{"type":"SqlMembership","source":{"filter":"users.EmployeeType != ''Vendor''"},"identityTypes":["users"]}]',
    '[{"type":"SqlMembership","source":{"filter":"agents.BlueprintId = \"quoted\""},"identityTypes":["agents","users"],"future":{"nested":[1,2]}}]',
    '[{"type":"SqlMembership","source":{"futureShape":{"version":2}},"identityTypes":["users"],"future":{"nested":[1,2]}}]'
)

foreach ($query in $preservationCases) {
    $converted = Convert-SqlSourceQuery -Query $query
    Assert-Equal $query $converted.TransformedQuery 'current explicit SQL parts survive the converter byte-for-byte'
}

$legacyGroupQuery = '[{"type":"SecurityGroup","source":["11111111-1111-1111-1111-111111111111","22222222-2222-2222-2222-222222222222"],"exclusionary":true}]'
$convertedLegacyGroup = Convert-SqlSourceQuery -Query $legacyGroupQuery
Assert-Equal 'Old' $convertedLegacyGroup.Category 'legacy SecurityGroup input retains its conversion category'
Assert-Equal `
    '[{"type":"GroupMembership","source":"11111111-1111-1111-1111-111111111111","exclusionary":true},{"type":"GroupMembership","source":"22222222-2222-2222-2222-222222222222","exclusionary":true}]' `
    $convertedLegacyGroup.TransformedQuery `
    'legacy SecurityGroup expansion remains unchanged'

$legacySqlQuery = '[{"type":"SqlMembership","source":[{"ids":[7,8],"depth":2,"filter":"Department != ''Vendor''"}],"identityTypes":["users"],"exclusionary":true,"future":"keep"}]'
$convertedLegacySql = Convert-SqlSourceQuery -Query $legacySqlQuery
Assert-Equal 'Old' $convertedLegacySql.Category 'legacy SQL ids input retains its conversion category'
Assert-Equal 2 `
    ([regex]::Matches($convertedLegacySql.TransformedQuery, '"type":"SqlMembership"').Count) `
    'legacy SQL ids still expand into one part per manager'
Assert-Equal 2 `
    ([regex]::Matches($convertedLegacySql.TransformedQuery, '"identityTypes":\["users"\]').Count) `
    'legacy SQL expansion preserves the explicit users-only selection on every part'
Assert-Equal 2 `
    ([regex]::Matches($convertedLegacySql.TransformedQuery, "Department <> 'Vendor'").Count) `
    'legacy SQL expansion retains the established not-equal normalization'
Assert-Equal 2 `
    ([regex]::Matches($convertedLegacySql.TransformedQuery, '"future":"keep"').Count) `
    'legacy SQL expansion preserves unrelated top-level properties'

Write-Host ("{0} source-query checks passed." -f $script:Passed) -ForegroundColor Green
