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

function Assert-Conversion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Query,

        [Parameter(Mandatory = $true)]
        [string]$Expected,

        [Parameter(Mandatory = $true)]
        [string]$Because
    )

    $converted = Convert-SqlSourceQuery -Query $Query
    Assert-Equal $Expected $converted.TransformedQuery $Because
    Assert-Equal 'Changed' $converted.Status "$Because (reported as changed)"
    Assert-Equal 0 $converted.UnconvertedSqlParts "$Because (nothing reported as unrecognised)"

    $convertedAgain = Convert-SqlSourceQuery -Query $converted.TransformedQuery
    Assert-Equal 'NoChanges' $convertedAgain.Status "$Because (a second run changes nothing)"
}

function Assert-KeptAsStored {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Query,

        [Parameter(Mandatory = $true)]
        [int]$UnconvertedSqlParts,

        [Parameter(Mandatory = $true)]
        [string]$KeptAsStoredReason,

        [Parameter(Mandatory = $true)]
        [string]$Because
    )

    $converted = Convert-SqlSourceQuery -Query $Query
    Assert-Equal $Query $converted.TransformedQuery "$Because (kept byte-for-byte): $Query"
    Assert-Equal 'NoChanges' $converted.Status "$Because (not rewritten): $Query"
    Assert-Equal $UnconvertedSqlParts $converted.UnconvertedSqlParts "$Because (unrecognised SQL parts reported): $Query"
    Assert-Equal $KeptAsStoredReason $converted.KeptAsStoredReason "$Because (reason reported): $Query"
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
    '[{"type":"SqlMembership","source":{"manager":{"id":2147483647,"depth":1}},"identityTypes":["users"]}]'
)

foreach ($query in $preservationCases) {
    Assert-KeptAsStored -Query $query -UnconvertedSqlParts 0 -KeptAsStoredReason 'None' `
        -Because 'a current explicit SQL part survives the converter'
}

$preservedUnrecognisedCases = @(
    '[{"type":"SqlMembership","source":{"futureShape":{"version":2}},"identityTypes":["users"],"future":{"nested":[1,2]}}]',
    '[{"type":"SqlMembership","source":{"manager":{"id":7,"extra":1}},"identityTypes":["users"]}]',
    '[{"type":"SqlMembership","source":{"manager":{"id":7,"depth":0}},"identityTypes":["users"]}]',
    '[{"type":"SqlMembership","source":{"manager":{"depth":2}},"identityTypes":["users"]}]',
    '[{"type":"SqlMembership","source":{"filter":5},"identityTypes":["users"]}]',
    '[{"type":"SqlMembership","source":{},"identityTypes":["users"]}]',
    '[{"type":"SqlMembership","source":{"IDS":[7]},"identityTypes":["users"]}]'
)

foreach ($query in $preservedUnrecognisedCases) {
    Assert-KeptAsStored -Query $query -UnconvertedSqlParts 0 -KeptAsStoredReason 'None' `
        -Because 'a current explicit SQL part with an unrecognised source is kept'
    Assert-Equal 1 (Convert-SqlSourceQuery -Query $query).SqlPartsOutsideSchema `
        "a current explicit SQL part with an unrecognised source is counted as outside the schema: $query"
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
Assert-Equal 0 $convertedLegacySql.UnconvertedSqlParts 'a convertible legacy SQL part is not reported as unrecognised'

$oldPortalSqlCases = @(
    @{
        Query = '[{"type":"SqlMembership","source":[{"ids":[7],"depth":2,"filter":"Department = ''IT''","includeOrg":true,"includeFilter":true}]}]'
        Expected = '[{"type":"SqlMembership","source":{"manager":{"id":7,"depth":2},"filter":"Department = ''IT''"}}]'
        Because = 'the old portal''s includeOrg and includeFilter flags are dropped from a manager list'
    },
    @{
        Query = '[{"type":"SqlMembership","source":[{"ids":[7,8],"depth":0,"includeOrg":true,"includeFilter":false}]}]'
        Expected = '[{"type":"SqlMembership","source":{"manager":{"id":7}}},{"type":"SqlMembership","source":{"manager":{"id":8}}}]'
        Because = 'a depth of 0, which means all levels, is dropped from a manager list'
    },
    @{
        Query = '[{"type":"SqlMembership","source":[{"ids":[7],"filter":"Department != ''IT''","depth":0}]}]'
        Expected = '[{"type":"SqlMembership","source":{"manager":{"id":7},"filter":"Department <> ''IT''"}}]'
        Because = 'the filter of a manager list with a depth of 0 is kept and normalised'
    },
    @{
        Query = '[{"type":"SqlMembership","source":{"ids":[7],"filter":""}}]'
        Expected = '[{"type":"SqlMembership","source":{"manager":{"id":7}}}]'
        Because = 'an empty filter, which means no filter, is dropped from a manager list'
    },
    @{
        Query = '[{"type":"SqlMembership","source":[{"ids":[7],"depth":null,"filter":null,"includeOrg":null,"includeFilter":null}]}]'
        Expected = '[{"type":"SqlMembership","source":{"manager":{"id":7}}}]'
        Because = 'null optional values are dropped from a manager list'
    },
    @{
        Query = '[{"type":"SqlMembership","source":[{"ids":[2147483647],"depth":2147483647}]}]'
        Expected = '[{"type":"SqlMembership","source":{"manager":{"id":2147483647,"depth":2147483647}}}]'
        Because = 'the largest manager ID and depth the service can read are converted'
    }
)

foreach ($case in $oldPortalSqlCases) {
    Assert-Conversion -Query $case.Query -Expected $case.Expected -Because $case.Because
}

Assert-Conversion `
    -Query '[{"type":"SqlMembership","source":{"manager":{"id":7,"note":"a != b"},"filter":"x != 1","extra":"c != d"},"future":"e != f"}]' `
    -Expected '[{"type":"SqlMembership","source":{"manager":{"id":7,"note":"a != b"},"filter":"x <> 1","extra":"c != d"},"future":"e != f"}]' `
    -Because 'only the filter of a manager source is normalised; every other value keeps its text'
Assert-Conversion `
    -Query '[{"type":"SqlMembership","source":[{"filter":"x != 1","extra":"c != d"}]}]' `
    -Expected '[{"type":"SqlMembership","source":{"filter":"x <> 1","extra":"c != d"}}]' `
    -Because 'only the filter of a filter source is normalised; every other value keeps its text'

$otherTypeQuery = '[{"type":"PlaceMembership","source":"SecurityGroup != room"}]'
Assert-KeptAsStored -Query $otherTypeQuery -UnconvertedSqlParts 0 -KeptAsStoredReason 'None' `
    -Because 'values in parts of other types are not rewritten as text'

Assert-Conversion `
    -Query '[{"type":"GroupOwnership","source":["SecurityGroup","SqlMembership"]}]' `
    -Expected '[{"type":"GroupOwnership","source":["GroupMembership","SqlMembership"]}]' `
    -Because 'a GroupOwnership part selecting the old SecurityGroup type selects GroupMembership'
Assert-Conversion `
    -Query '[{"type":"GroupOwnership","source":["SecurityGroup"]}]' `
    -Expected '[{"type":"GroupOwnership","source":["GroupMembership"]}]' `
    -Because 'a GroupOwnership part selecting only the old SecurityGroup type keeps its array'
Assert-KeptAsStored -Query '[{"type":"GroupOwnership","source":["All"]}]' -UnconvertedSqlParts 0 -KeptAsStoredReason 'None' `
    -Because 'a current GroupOwnership part is not rewritten'
Assert-Conversion `
    -Query '[{"type":"SecurityGroup","source":[]}]' `
    -Expected '[{"type":"GroupMembership","source":[]}]' `
    -Because 'a SecurityGroup part without group IDs is renamed'

if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) {
    Assert-Conversion `
        -Query '[{"type":"SqlMembership","source":[{"filter":"2024-01-01T10:00:00+02:00"}],"future":"2024-01-01T10:00:00+02:00"}]' `
        -Expected '[{"type":"SqlMembership","source":{"filter":"2024-01-01T10:00:00+02:00"},"future":"2024-01-01T10:00:00+02:00"}]' `
        -Because 'date-like text in a rewritten SQL part is kept exactly as stored'
    Assert-KeptAsStored -Query '[{"type":"PlaceMembership","source":"2024-01-01T10:00:00+02:00"}]' -UnconvertedSqlParts 0 -KeptAsStoredReason 'None' `
        -Because 'date-like text in a part of another type is kept exactly as stored'
}
else {
    Write-Warning 'ConvertFrom-Json has no -DateKind parameter on this PowerShell version, so the checks that date-like text is rewritten exactly were skipped.'
}

$unrecognisedSqlSources = @(
    '"not-an-object"',
    '42',
    'false',
    '0',
    '""',
    'null',
    '[]',
    '{}',
    '{"futureShape":{"version":2}}',
    '{"manager":{"depth":2}}',
    '{"ids":[7],"future":1}',
    '{"IDS":[7]}',
    '{"ids":[7],"Depth":2}',
    '{"ids":[{"id":7}]}',
    '{"ids":["7"]}',
    '{"ids":[0]}',
    '{"ids":[-7]}',
    '{"ids":[null]}',
    '{"ids":[[7]]}',
    '{"ids":[2147483648]}',
    '{"ids":[7.0]}',
    '{"ids":[true]}',
    '{"ids":[]}',
    '{"ids":7}',
    '{"ids":null}',
    '{"ids":[7],"depth":-1}',
    '{"ids":[7],"depth":"2"}',
    '{"ids":[7],"depth":false}',
    '{"ids":[7],"depth":2147483648}',
    '{"ids":[7],"filter":5}',
    '{"ids":[7],"includeOrg":"yes"}',
    '[{"filter":"a = 1"},{"unknown":true}]',
    '[{"filter":"a != 1"},null]'
)

$unrecognisedSqlCases = @('[{"type":"SqlMembership","note":"keep!=literal"}]') +
    @($unrecognisedSqlSources | ForEach-Object { '[{"type":"SqlMembership","source":' + $_ + ',"note":"keep!=literal"}]' })

foreach ($query in $unrecognisedSqlCases) {
    Assert-KeptAsStored -Query $query -UnconvertedSqlParts 1 -KeptAsStoredReason 'None' `
        -Because 'an unrecognised SQL part is kept instead of being dropped'
    Assert-Equal 1 (Convert-SqlSourceQuery -Query $query).SqlPartsOutsideSchema `
        "an unrecognised SQL part is counted as outside the schema: $query"

    $lenientVariant = $query.Substring(0, $query.Length - 1) + ',]'
    Assert-KeptAsStored -Query $lenientVariant -UnconvertedSqlParts 1 -KeptAsStoredReason 'UnrecoverableSqlPart' `
        -Because 'an unrecognised SQL part whose original text cannot be recovered keeps the whole query'
    Assert-Equal 1 (Convert-SqlSourceQuery -Query $lenientVariant).SqlPartsOutsideSchema `
        "an unrecognised SQL part that keeps the whole query is still counted as outside the schema: $lenientVariant"
}

$spacedQueries = @(
    '[ {"type":"SqlMembership","source":{"filter":"a = 1"},"identityTypes":["users"]} ]',
    "[`r`n  {`"type`":`"SqlMembership`",`"source`":{`"filter`":`"a = 1`"},`"identityTypes`":[`"users`"]},`r`n  {`"type`":`"SqlMembership`",`"source`":`"not-an-object`"}`r`n]"
)

$spacedUnconvertedParts = @(0, 1)
for ($spacedIndex = 0; $spacedIndex -lt $spacedQueries.Count; $spacedIndex++) {
    Assert-KeptAsStored -Query $spacedQueries[$spacedIndex] -UnconvertedSqlParts $spacedUnconvertedParts[$spacedIndex] -KeptAsStoredReason 'None' `
        -Because 'a query whose parts are all kept is not rewritten just to remove whitespace'
}

$unreadableQueries = @(
    '[{"type":',
    'not json',
    '[{"type":"SqlMembership","Type":"SqlMembership","source":{"filter":"a = 1"}}]'
)

foreach ($errorPreference in @('Stop', 'Continue')) {
    $ErrorActionPreference = $errorPreference
    try {
        foreach ($query in $unreadableQueries) {
            Assert-KeptAsStored -Query $query -UnconvertedSqlParts 0 -KeptAsStoredReason 'UnreadableJson' `
                -Because "a query that cannot be read is left as stored with ErrorActionPreference $errorPreference"
        }
    }
    finally {
        $ErrorActionPreference = 'Stop'
    }
}

$mixedQuery = '[{"type":"SqlMembership","source":"not-an-object","exclusionary":false},{"type":"SecurityGroup","source":["11111111-1111-1111-1111-111111111111"]}]'
$convertedMixed = Convert-SqlSourceQuery -Query $mixedQuery
Assert-Equal 'Changed' $convertedMixed.Status 'other legacy parts are still converted next to an unrecognised SQL part'
Assert-Equal `
    '[{"type":"SqlMembership","source":"not-an-object","exclusionary":false},{"type":"GroupMembership","source":"11111111-1111-1111-1111-111111111111"}]' `
    $convertedMixed.TransformedQuery `
    'only the recognised part is rewritten and the unrecognised SQL part keeps its original text'
Assert-Equal 1 $convertedMixed.UnconvertedSqlParts 'the unrecognised SQL part in a mixed query is reported'
Assert-Equal 'Old' $convertedMixed.Category 'a kept unrecognised SQL part does not hide the legacy conversion in a mixed query'
Assert-Equal 'NoChanges' (Convert-SqlSourceQuery -Query $convertedMixed.TransformedQuery).Status 'a second run over a converted mixed query changes nothing'

$lenientQuery = '[{"type":"SqlMembership","source":"not-an-object"},{"type":"SecurityGroup","source":["11111111-1111-1111-1111-111111111111"]},]'
Assert-KeptAsStored -Query $lenientQuery -UnconvertedSqlParts 1 -KeptAsStoredReason 'UnrecoverableSqlPart' `
    -Because 'a query whose original part text cannot be recovered is left exactly as stored'

$notEqualCases = @(
    @{ Filter = 'a != 1'; Expected = 'a <> 1'; Because = 'the != operator becomes <>' },
    @{ Filter = 'a!=1 AND b!=2'; Expected = 'a<>1 AND b<>2'; Because = 'every != operator is replaced, with or without spaces' },
    @{ Filter = "DisplayName = 'A!=B' AND b != 2"; Expected = "DisplayName = 'A!=B' AND b <> 2"; Because = 'text inside a quoted string is not changed' },
    @{ Filter = "Name = 'it''s != x' AND b != 2"; Expected = "Name = 'it''s != x' AND b <> 2"; Because = 'an escaped quote does not end a quoted string' },
    @{ Filter = "Name = N'x != y' AND b != 2"; Expected = "Name = N'x != y' AND b <> 2"; Because = 'text inside a Unicode string is not changed' },
    @{ Filter = '[odd!=name] != 1'; Expected = '[odd!=name] <> 1'; Because = 'a bracketed identifier is not changed' },
    @{ Filter = '[a]]!=b] != 1'; Expected = '[a]]!=b] <> 1'; Because = 'an escaped closing bracket does not end a bracketed identifier' },
    @{ Filter = '"col!=x" != 1'; Expected = '"col!=x" <> 1'; Because = 'a double-quoted identifier is not changed' },
    @{ Filter = '"a""!=b" != 1'; Expected = '"a""!=b" <> 1'; Because = 'an escaped double quote does not end a quoted identifier' },
    @{ Filter = "a != 1 -- note != x`r`nAND b != 2"; Expected = "a <> 1 -- note != x`r`nAND b <> 2"; Because = 'a line comment is not changed and ends at a Windows line break' },
    @{ Filter = "a != 1 -- note != x`nAND b != 2"; Expected = "a <> 1 -- note != x`nAND b <> 2"; Because = 'a line comment ends at a Unix line break' },
    @{ Filter = 'a != 1 /* x != /* nested != */ still != */ AND b != 2'; Expected = 'a <> 1 /* x != /* nested != */ still != */ AND b <> 2'; Because = 'nested block comments are not changed' },
    @{ Filter = "a != 1 AND b = 'open != x"; Expected = "a <> 1 AND b = 'open != x"; Because = 'text after an unterminated quote is not changed' },
    @{ Filter = 'a != 1 /* open != x'; Expected = 'a <> 1 /* open != x'; Because = 'text after an unterminated block comment is not changed' },
    @{ Filter = 'a - -1 != 2'; Expected = 'a - -1 <> 2'; Because = 'separated minus signs do not start a comment' },
    @{ Filter = 'a = 1'; Expected = 'a = 1'; Because = 'a filter without != is returned as it is' },
    @{ Filter = ''; Expected = ''; Because = 'an empty filter is returned as it is' }
)

foreach ($case in $notEqualCases) {
    Assert-Equal $case.Expected (Convert-SqlFilterNotEqualOperator -Filter $case.Filter) $case.Because
}

Assert-Conversion `
    -Query '[{"type":"SqlMembership","source":[{"ids":[7],"filter":"DisplayName = ''A!=B'' AND Department != ''IT''"}]}]' `
    -Expected '[{"type":"SqlMembership","source":{"manager":{"id":7},"filter":"DisplayName = ''A!=B'' AND Department <> ''IT''"}}]' `
    -Because 'a manager list keeps != inside quoted text and normalises the operator'
Assert-KeptAsStored -Query '[{"type":"SqlMembership","source":{"manager":{"id":7},"filter":"Note = ''x != y''"}}]' -UnconvertedSqlParts 0 -KeptAsStoredReason 'None' `
    -Because 'a filter whose only != is inside quoted text is not rewritten'

$differentCaseFilterQuery = '[{"type":"SqlMembership","source":{"Filter":"a != 1"}}]'
Assert-KeptAsStored -Query $differentCaseFilterQuery -UnconvertedSqlParts 0 -KeptAsStoredReason 'None' `
    -Because 'a property named Filter with different casing is not treated as the filter'
Assert-Equal 1 (Convert-SqlSourceQuery -Query $differentCaseFilterQuery).SqlPartsOutsideSchema `
    'a property named Filter with different casing is reported as outside the current schema'

$oldPortalFlagCases = @(
    @{
        Query = '[{"type":"SqlMembership","source":{"filter":"Department != ''IT''","includeOrg":false,"includeFilter":true}}]'
        Expected = '[{"type":"SqlMembership","source":{"filter":"Department <> ''IT''"}}]'
        Because = 'the old portal''s includeOrg and includeFilter flags are dropped from a filter source'
    },
    @{
        Query = '[{"type":"SqlMembership","source":[{"filter":"Department = ''IT''","includeOrg":null,"includeFilter":true}]}]'
        Expected = '[{"type":"SqlMembership","source":{"filter":"Department = ''IT''"}}]'
        Because = 'null and true flags are dropped from a filter source in a list'
    },
    @{
        Query = '[{"type":"SqlMembership","source":{"manager":{"id":7},"includeOrg":true,"includeFilter":false}}]'
        Expected = '[{"type":"SqlMembership","source":{"manager":{"id":7}}}]'
        Because = 'the old portal''s includeOrg and includeFilter flags are dropped from a manager source'
    }
)

foreach ($case in $oldPortalFlagCases) {
    Assert-Conversion -Query $case.Query -Expected $case.Expected -Because $case.Because
    Assert-Equal 0 (Convert-SqlSourceQuery -Query $case.Query).SqlPartsOutsideSchema "$($case.Because) (the result matches the current schema)"
}

$unchangedPartQueries = @(
    '[{"type":"GroupMembership","source":"11111111-1111-1111-1111-111111111111","exclusionary":false}]',
    '[{"type":"GroupMembership","source":"11111111-1111-1111-1111-111111111111","future":1e3}]',
    '[ {"type": "GroupMembership", "source": "11111111-1111-1111-1111-111111111111"} ]',
    '[{"type":"GroupOwnership","source":["All"],"weight":1e3}]',
    '[ {"type": "GroupOwnership", "source": ["All"]} ]',
    '[{"type":"PlaceMembership","source":{"weight":1e3}}]',
    '[ {"type": "SqlMembership", "source": {"filter": "a = 1"}} ]',
    '[{"type":"SqlMembership","source":{"manager":{"id":7}},"weight":1e3}]'
)

foreach ($query in $unchangedPartQueries) {
    Assert-KeptAsStored -Query $query -UnconvertedSqlParts 0 -KeptAsStoredReason 'None' `
        -Because 'a part that needs no change keeps its exact text'
}

$mixedRawQuery = '[{"type":"PlaceMembership", "source":{"weight":1e3}},{"type":"GroupMembership","source":"11111111-1111-1111-1111-111111111111","exclusionary":false},{"type":"SecurityGroup","source":["22222222-2222-2222-2222-222222222222"]}]'
$mixedRawExpected = '[{"type":"PlaceMembership", "source":{"weight":1e3}},{"type":"GroupMembership","source":"11111111-1111-1111-1111-111111111111","exclusionary":false},{"type":"GroupMembership","source":"22222222-2222-2222-2222-222222222222"}]'
Assert-Conversion -Query $mixedRawQuery -Expected $mixedRawExpected `
    -Because 'parts that need no change keep their exact text next to a converted part'
Assert-Equal 'Old' (Convert-SqlSourceQuery -Query $mixedRawQuery).Category 'parts kept as stored do not change the category of a converted query'

$datedSqlQuery = '[{"type":"SqlMembership","source":[{"ids":[7],"filter":"a != 1"}],"createdAt":"2024-01-01T10:00:00+02:00"}]'
$datedMixedQuery = '[{"type":"SqlMembership","source":{"manager":{"id":7},"filter":"a != 1"},"createdAt":"2024-01-01T10:00:00+02:00"},{"type":"SecurityGroup","source":["11111111-1111-1111-1111-111111111111"]}]'
$datedMixedExpected = '[{"type":"SqlMembership","source":{"manager":{"id":7},"filter":"a != 1"},"createdAt":"2024-01-01T10:00:00+02:00"},{"type":"GroupMembership","source":"11111111-1111-1111-1111-111111111111"}]'
$datedLenientQuery = '[{"type":"SqlMembership","source":{"filter":"a != 1"},"createdAt":"2024-01-01T10:00:00+02:00"},{"type":"SecurityGroup","source":["11111111-1111-1111-1111-111111111111"]},]'

# Simulates a PowerShell version without ConvertFrom-Json -DateKind, which reads date-like text
# as DateTime values.
$script:sourceQueryReaderHasDateKind = $false
try {
    Assert-KeptAsStored -Query $datedSqlQuery -UnconvertedSqlParts 0 -KeptAsStoredReason 'None' `
        -Because 'without -DateKind a part holding date-like text keeps its exact text'
    Assert-Equal 1 (Convert-SqlSourceQuery -Query $datedSqlQuery).UnrewritableParts `
        'without -DateKind a part holding date-like text is reported'

    $convertedDatedMixed = Convert-SqlSourceQuery -Query $datedMixedQuery
    Assert-Equal $datedMixedExpected $convertedDatedMixed.TransformedQuery `
        'without -DateKind the part holding date-like text is kept and the other part is converted'
    Assert-Equal 'Changed' $convertedDatedMixed.Status 'without -DateKind the other part of a mixed query is still converted'
    Assert-Equal 1 $convertedDatedMixed.UnrewritableParts 'without -DateKind the kept part of a mixed query is reported'
    Assert-Equal 'Old' $convertedDatedMixed.Category 'without -DateKind a mixed query keeps its conversion category'

    Assert-KeptAsStored -Query $datedLenientQuery -UnconvertedSqlParts 0 -KeptAsStoredReason 'UnrewritablePart' `
        -Because 'without -DateKind a query whose original part text cannot be recovered is left exactly as stored'
    Assert-Equal 1 (Convert-SqlSourceQuery -Query $datedLenientQuery).UnrewritableParts `
        'without -DateKind the part that forced the whole query to be kept is reported'

    Assert-Conversion `
        -Query '[{"type":"SqlMembership","source":[{"ids":[7],"filter":"a != 1"}]}]' `
        -Expected '[{"type":"SqlMembership","source":{"manager":{"id":7},"filter":"a <> 1"}}]' `
        -Because 'without -DateKind a query without date-like text is still converted'
}
finally {
    Remove-Variable -Name sourceQueryReaderHasDateKind -Scope Script
}

if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) {
    Assert-Conversion -Query $datedMixedQuery `
        -Expected '[{"type":"SqlMembership","source":{"manager":{"id":7},"filter":"a <> 1"},"createdAt":"2024-01-01T10:00:00+02:00"},{"type":"GroupMembership","source":"11111111-1111-1111-1111-111111111111"}]' `
        -Because 'with -DateKind a part holding date-like text is converted and keeps that text'
}

$deeplyNestedValue = ('{"a":' * 95) + '1' + ('}' * 95)
$deepQuery = '[{"type":"SqlMembership","source":[{"ids":[7]}],"future":' + $deeplyNestedValue + '}]'
Assert-KeptAsStored -Query $deepQuery -UnconvertedSqlParts 0 -KeptAsStoredReason 'None' `
    -Because 'a part nested too deeply to be written back in full keeps its exact text'
Assert-Equal 1 (Convert-SqlSourceQuery -Query $deepQuery).UnrewritableParts 'a part nested too deeply is reported'

$deepMixedQuery = '[{"type":"PlaceMembership","source":' + $deeplyNestedValue + '},{"type":"SecurityGroup","source":["11111111-1111-1111-1111-111111111111"]}]'
Assert-Conversion -Query $deepMixedQuery `
    -Expected ('[{"type":"PlaceMembership","source":' + $deeplyNestedValue + '},{"type":"GroupMembership","source":"11111111-1111-1111-1111-111111111111"}]') `
    -Because 'a deeply nested part keeps its exact text next to a converted part'

$nestedValue = ('{"a":' * 80) + '1' + ('}' * 80)
Assert-Conversion `
    -Query ('[{"type":"SqlMembership","source":[{"ids":[7]}],"future":' + $nestedValue + '}]') `
    -Expected ('[{"type":"SqlMembership","source":{"manager":{"id":7}},"future":' + $nestedValue + '}]') `
    -Because 'a part nested within the depth that can be written back is converted in full'

$outsideSchemaCases = @(
    @{ Query = '[{"type":"SqlMembership","source":[{"filter":"x != 1","extra":"c != d"}]}]'; Expected = 1; Because = 'a converted filter source with an extra property' },
    @{ Query = '[{"type":"SqlMembership","source":{"manager":{"id":7,"note":"a"}}}]'; Expected = 1; Because = 'a manager source with an extra manager property' },
    @{ Query = '[{"type":"SqlMembership","source":{"manager":{"id":7,"depth":0}}}]'; Expected = 1; Because = 'a manager source with a depth of 0' },
    @{ Query = '[{"type":"SqlMembership","source":{"filter":"a = 1","includeOrg":"yes"}}]'; Expected = 1; Because = 'a filter source with a flag that is not true, false or null' },
    @{ Query = '[{"type":"SqlMembership","source":[{"ids":[7,8],"depth":2,"filter":"a != 1"}]}]'; Expected = 0; Because = 'a converted manager list' },
    @{ Query = '[{"type":"SqlMembership","source":{"filter":"a = 1"}}]'; Expected = 0; Because = 'a current filter source' }
)

foreach ($case in $outsideSchemaCases) {
    $converted = Convert-SqlSourceQuery -Query $case.Query
    Assert-Equal $case.Expected $converted.SqlPartsOutsideSchema "SQL parts outside the current schema are counted for $($case.Because)"
    Assert-Equal 0 $converted.UnconvertedSqlParts "$($case.Because) is not reported as unrecognised"
}

$keptOutsideSchemaCases = @(
    @{ Query = '[{"type":"SqlMembership","source":"bad","identityTypes":["users"]}]'; Unconverted = 1; Unrewritable = 0; OutsideSchema = 1; Because = 'an explicit SQL part whose source is not an object' },
    @{ Query = '[{"type":"SqlMembership","source":{"futureShape":1},"identityTypes":["users"]}]'; Unconverted = 0; Unrewritable = 0; OutsideSchema = 1; Because = 'an explicit SQL part with an unknown source property' },
    @{ Query = '[{"type":"SqlMembership","source":{"manager":{"id":7,"extra":1}},"identityTypes":["users"]}]'; Unconverted = 0; Unrewritable = 0; OutsideSchema = 1; Because = 'an explicit SQL part with an extra manager property' },
    @{ Query = '[{"type":"SqlMembership","source":[{"ids":[7]}],"future":' + $deeplyNestedValue + '}]'; Unconverted = 0; Unrewritable = 1; OutsideSchema = 1; Because = 'a SQL part in the old format nested too deeply to be written back' },
    @{ Query = '[{"type":"SqlMembership","source":{"filter":"a = 1"},"future":' + $deeplyNestedValue + '}]'; Unconverted = 0; Unrewritable = 1; OutsideSchema = 0; Because = 'a current SQL part nested too deeply to be written back' },
    @{ Query = '[{"type":"PlaceMembership","source":' + $deeplyNestedValue + '}]'; Unconverted = 0; Unrewritable = 1; OutsideSchema = 0; Because = 'a part of another type nested too deeply to be written back' }
)

foreach ($case in $keptOutsideSchemaCases) {
    $converted = Convert-SqlSourceQuery -Query $case.Query
    Assert-Equal $case.Query $converted.TransformedQuery "$($case.Because) is kept byte-for-byte"
    Assert-Equal $case.Unconverted $converted.UnconvertedSqlParts "unrecognised SQL parts are counted for $($case.Because)"
    Assert-Equal $case.Unrewritable $converted.UnrewritableParts "parts that cannot be written back are counted for $($case.Because)"
    Assert-Equal $case.OutsideSchema $converted.SqlPartsOutsideSchema "SQL parts outside the current schema are counted for $($case.Because)"
}

$groupId = '11111111-1111-1111-1111-111111111111'
$repeatedNameCases = @(
    @{ Query = '[{"type":"SqlMembership","source":{"ids":[7]},"source":{"ids":[8]}}]'; OutsideSchema = 1; Because = 'a repeated source' },
    @{ Query = '[{"type":"SqlMembership","source":[{"ids":[7],"filter":"a = 1","filter":"b != 2"}]}]'; OutsideSchema = 1; Because = 'a repeated filter inside an old-format source' },
    @{ Query = '[{"type":"SqlMembership","type":"SqlMembership","source":[{"ids":[7]}]}]'; OutsideSchema = 1; Because = 'a repeated type' },
    @{ Query = '[{"type":"SqlMembership","source":[{"ids":[7],"ids":[8]}]}]'; OutsideSchema = 1; Because = 'a repeated list of manager ids' },
    @{ Query = '[{"type":"SqlMembership","source":{"manager":{"id":7,"id":8}}}]'; OutsideSchema = 0; Because = 'a repeated manager id in a current source' },
    @{ Query = '[{"type":"SqlMembership","source":[{"ids":[7]}],"future":{"a":1,"a":2}}]'; OutsideSchema = 1; Because = 'a repeated name inside an extra property' },
    @{ Query = '[{"type":"SqlMembership","source":[{"ids":[7]}],"note":1,"not\u0065":2}]'; OutsideSchema = 1; Because = 'a name repeated through an escape sequence' },
    @{ Query = '[{"type":"SecurityGroup","source":["' + $groupId + '"],"exclusionary":false,"exclusionary":true}]'; OutsideSchema = 0; Because = 'a repeated flag in a legacy group part' }
)

foreach ($case in $repeatedNameCases) {
    Assert-KeptAsStored -Query $case.Query -UnconvertedSqlParts 0 -KeptAsStoredReason 'None' `
        -Because "a part with $($case.Because) is not rebuilt, so no value is lost"
    $converted = Convert-SqlSourceQuery -Query $case.Query
    Assert-Equal 1 $converted.UnrewritableParts "a part with $($case.Because) is reported"
    Assert-Equal $case.OutsideSchema $converted.SqlPartsOutsideSchema "SQL parts outside the current schema are counted for a part with $($case.Because)"
}

Assert-Conversion `
    -Query ('[{"type":"SqlMembership","source":{"ids":[7]},"source":{"ids":[8]}},{"type":"SecurityGroup","source":["' + $groupId + '"]}]') `
    -Expected ('[{"type":"SqlMembership","source":{"ids":[7]},"source":{"ids":[8]}},{"type":"GroupMembership","source":"' + $groupId + '"}]') `
    -Because 'a part that repeats a name keeps its exact text next to a converted part'

$keptCurrentRepeatCases = @(
    @{ Query = '[{"type":"SqlMembership","source":{"manager":{"id":7,"id":8}},"identityTypes":["users"]}]'; Because = 'a repeated manager id' },
    @{ Query = '[{"type":"SqlMembership","source":{"filter":"a = 1","filter":"b = 2"},"identityTypes":["users"]}]'; Because = 'a repeated filter' },
    @{ Query = '[{"type":"SqlMembership","source":{"manager":{"id":7}},"identityTypes":["users"],"identityTypes":["agents"]}]'; Because = 'a repeated identity selection' }
)

foreach ($case in $keptCurrentRepeatCases) {
    Assert-KeptAsStored -Query $case.Query -UnconvertedSqlParts 0 -KeptAsStoredReason 'None' `
        -Because "a current SQL part with $($case.Because) is kept exactly as stored"
    $converted = Convert-SqlSourceQuery -Query $case.Query
    Assert-Equal 1 $converted.UnrewritableParts "a current SQL part with $($case.Because) is reported"
    Assert-Equal 0 $converted.SqlPartsOutsideSchema "a current SQL part with $($case.Because) is not counted outside the schema"
}

$keptCurrentRepeatPart = '{"type":"SqlMembership","source":{"manager":{"id":7,"id":8}},"identityTypes":["users"]}'
Assert-Conversion `
    -Query ('[' + $keptCurrentRepeatPart + ',{"type":"SecurityGroup","source":["' + $groupId + '"]}]') `
    -Expected ('[' + $keptCurrentRepeatPart + ',{"type":"GroupMembership","source":"' + $groupId + '"}]') `
    -Because 'a current SQL part that repeats a name keeps its exact text next to a converted part'
Assert-Equal 1 (Convert-SqlSourceQuery -Query ('[' + $keptCurrentRepeatPart + ',{"type":"SecurityGroup","source":["' + $groupId + '"]}]')).UnrewritableParts `
    'a current SQL part that repeats a name is reported next to a converted part'

Assert-Equal 'None' (Convert-SqlSourceQuery -Query '[{"type":"SqlMembership","source":[{"ids":[7]}],"a":{"x":1},"b":{"x":2}}]').KeptAsStoredReason `
    'the same name in two different objects is not a repeat'
Assert-Conversion `
    -Query '[{"type":"SqlMembership","source":[{"ids":[7]}],"a":{"x":1},"b":{"x":2}}]' `
    -Expected '[{"type":"SqlMembership","source":{"manager":{"id":7}},"a":{"x":1},"b":{"x":2}}]' `
    -Because 'the same name in two different objects does not stop a conversion'

$possibleRepeatCases = @(
    @{ Query = '[{"type":"SqlMembership","source":[{"ids":[7],"filter":"a = 1","filter":"b != 2"}]},]'; Because = 'a query with a trailing comma that repeats a name' },
    @{ Query = '[{"type":"SecurityGroup","source":["' + $groupId + '"],"exclusionary":false,"exclusionary":true},]'; Because = 'a legacy group query with a trailing comma that repeats a flag' },
    @{ Query = "[{'type':'SecurityGroup','source':['$groupId']}]"; Because = 'a query in a syntax that cannot be checked for repeated names' }
)

foreach ($case in $possibleRepeatCases) {
    Assert-KeptAsStored -Query $case.Query -UnconvertedSqlParts 0 -KeptAsStoredReason 'PossibleRepeatedNames' `
        -Because "$($case.Because) is left whole"
    $converted = Convert-SqlSourceQuery -Query $case.Query
    Assert-Equal 0 $converted.UnrewritableParts "$($case.Because) reports no individual part"
    Assert-Equal 0 $converted.SqlPartsOutsideSchema "$($case.Because) reports no schema count"
}

Assert-Conversion `
    -Query ('[{"type":"SqlMembership","source":[{"ids":[7]}]},{"type":"SecurityGroup","source":["' + $groupId + '"]},]') `
    -Expected ('[{"type":"SqlMembership","source":{"manager":{"id":7}}},{"type":"GroupMembership","source":"' + $groupId + '"}]') `
    -Because 'a query with a trailing comma and no repeated names is still converted'

$originalSqlOperation = ${function:Invoke-SqlOperationWithFirewallRetry}
$originalConnectionFactory = ${function:New-SourceQueryMigrationConnection}
try {
    $script:loggingRows = [Data.DataTable]::new()
    [void]$script:loggingRows.Columns.Add('Id', [string])
    [void]$script:loggingRows.Columns.Add('Query', [string])
    $mixedLoggingJob = '00000000-0000-0000-0000-00000000000a'
    $repeatedLoggingJob = '00000000-0000-0000-0000-00000000000b'
    $unreadableLoggingJob = '00000000-0000-0000-0000-00000000000c'
    $convertedLoggingJob = '00000000-0000-0000-0000-00000000000d'
    $keptRepeatLoggingJob = '00000000-0000-0000-0000-00000000000e'
    [void]$script:loggingRows.Rows.Add($mixedLoggingJob,
        '[{"type":"SqlMembership","source":"not-an-object"},{"type":"SqlMembership","source":[{"ids":[7]}],"future":' + $deeplyNestedValue + '},]')
    [void]$script:loggingRows.Rows.Add($repeatedLoggingJob,
        '[{"type":"SecurityGroup","source":["' + $groupId + '"],"exclusionary":false,"exclusionary":true},]')
    [void]$script:loggingRows.Rows.Add($unreadableLoggingJob, 'not json')
    [void]$script:loggingRows.Rows.Add($convertedLoggingJob, '[{"type":"SecurityGroup","source":["' + $groupId + '"]}]')
    [void]$script:loggingRows.Rows.Add($keptRepeatLoggingJob, '[' + $keptCurrentRepeatPart + ']')

    $script:sqlOperationCalls = 0
    function New-SourceQueryMigrationConnection {
        $connection = [PSCustomObject]@{}
        $connection | Add-Member -MemberType ScriptMethod -Name Dispose -Value {}
        return $connection
    }
    function Invoke-SqlOperationWithFirewallRetry {
        $script:sqlOperationCalls++
        switch ($script:sqlOperationCalls) {
            1 { return 1 }
            2 { return $script:loggingRows }
            default { return 1 }
        }
    }

    $loggedLines = @(Set-UpdateSourceQuery -EnvironmentAbbreviation 'test' -SolutionAbbreviation 'gmm' -ConnectionString 'unused' 6>&1 |
            ForEach-Object { [string]$_ })
    $mixedLines = @($loggedLines | Where-Object { $_.StartsWith("Job ${mixedLoggingJob}:") })

    Assert-Equal 3 $script:sqlOperationCalls 'the migration checks the table, reads the jobs and writes the converted query'
    Assert-Equal 4 $mixedLines.Count 'a query kept whole still logs every other finding for that job'
    Assert-True ($mixedLines[0] -like '*left the whole query unchanged because a SQL part in an unrecognised format*') 'the reason the query was kept whole is logged'
    Assert-True ($mixedLines[1] -like '*left 1 SQL part(s) unchanged because their format is not recognised*') 'the unrecognised SQL part is logged'
    Assert-True ($mixedLines[2] -like '*left 1 part(s) unchanged because their values cannot be written back exactly*') 'the part that cannot be written back is logged'
    Assert-True ($mixedLines[3] -like '*2 SQL part(s) do not match the current schema*') 'the SQL parts outside the schema are logged'
    Assert-True (@($loggedLines | Where-Object { $_ -like "Job ${repeatedLoggingJob}: left the whole query unchanged because it may repeat a property name*" }).Count -eq 1) `
        'a query that may repeat a property name is logged'
    Assert-True (@($loggedLines | Where-Object { $_ -like "Job ${unreadableLoggingJob}: left the query unchanged because it is not valid JSON*" }).Count -eq 1) `
        'an unreadable query is logged'
    Assert-Equal 0 (@($loggedLines | Where-Object { $_.StartsWith("Job ${convertedLoggingJob}:") })).Count 'a converted query logs no finding'
    $keptRepeatLines = @($loggedLines | Where-Object { $_.StartsWith("Job ${keptRepeatLoggingJob}:") })
    Assert-Equal 1 $keptRepeatLines.Count 'a kept current SQL part that repeats a name logs one finding'
    Assert-True ($keptRepeatLines[0] -like '*left 1 part(s) unchanged because their values cannot be written back exactly*') `
        'a kept current SQL part that repeats a name is logged as a part that cannot be written back'

    $expectedSummary = @(
        'Queries updated from old format: 1',
        'Queries not updated: 4',
        'Queries with SQL parts left unchanged because their format is not recognised: 1',
        'Queries left whole because such a SQL part could not be kept exactly as stored: 1',
        'Queries with parts left unchanged because their values cannot be written back exactly: 2',
        'Queries left whole because such a part could not be kept exactly as stored: 0',
        'Queries with SQL parts that do not match the current schema and may stop with a schema error: 1',
        'Queries left whole because they may repeat a property name: 1',
        'Queries left unchanged because they are not valid JSON: 1'
    )
    foreach ($summaryLine in $expectedSummary) {
        Assert-True ($loggedLines -contains $summaryLine) "the summary reports '$summaryLine'"
    }
}
finally {
    Set-Item -LiteralPath 'function:Invoke-SqlOperationWithFirewallRetry' -Value $originalSqlOperation
    Set-Item -LiteralPath 'function:New-SourceQueryMigrationConnection' -Value $originalConnectionFactory
}

Write-Host ("{0} source-query checks passed." -f $script:Passed) -ForegroundColor Green
