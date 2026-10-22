# Copyright (c) Microsoft Corporation.
# Licensed under the MIT license.
#
# Dependency-free consistency checks for the customer Microsoft Graph application-role baseline.

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$script:Passed = 0

$expectedRoles = @(
    'Application.Read.All',
    'Group.Create',
    'GroupMember.Read.All',
    'Member.Read.Hidden',
    'User.Read.All'
)
$expectedDemoRoles = @(
    'AgentIdentity.Create.All',
    'AgentIdentity.Read.All',
    'AgentIdentityBlueprint.Create',
    'AgentIdentityBlueprint.Read.All',
    'AgentIdentityBlueprintPrincipal.Create',
    'AgentIdentityBlueprintPrincipal.Read.All',
    'User.ReadWrite.All'
)

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

function Assert-RoleSet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$ActualRoles,

        [Parameter(Mandatory = $true)]
        [string]$Because
    )

    $actual = @($ActualRoles | Sort-Object -Unique)
    $expected = @($expectedRoles | Sort-Object)

    if (($actual -join ',') -ne ($expected -join ',')) {
        throw "Assertion failed: $Because. Expected '$($expected -join ', ')', actual '$($actual -join ', ')'."
    }

    $script:Passed++
}

function Get-RoleNames {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Text
    )

    return @(
        [regex]::Matches(
            $Text,
            '(?<![A-Za-z])(?:Application\.Read\.All|Group\.Create|GroupMember\.Read\.All|Member\.Read\.Hidden|User\.Read\.All)(?![A-Za-z])') |
            ForEach-Object { $_.Value }
    )
}

function Get-DemoRoleNames {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Text
    )

    return @(
        [regex]::Matches(
            $Text,
            '(?<![A-Za-z])(?:AgentIdentity\.Create\.All|AgentIdentity\.Read\.All|AgentIdentityBlueprint\.Create|AgentIdentityBlueprint\.Read\.All|AgentIdentityBlueprintPrincipal\.Create|AgentIdentityBlueprintPrincipal\.Read\.All|User\.ReadWrite\.All)(?![A-Za-z])') |
            ForEach-Object { $_.Value }
    )
}

function Assert-DemoRoleSet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$ActualRoles,

        [Parameter(Mandatory = $true)]
        [string]$Because
    )

    $actual = @($ActualRoles | Sort-Object -Unique)
    $expected = @($expectedDemoRoles | Sort-Object)

    if (($actual -join ',') -ne ($expected -join ',')) {
        throw "Assertion failed: $Because. Expected '$($expected -join ', ')', actual '$($actual -join ', ')'."
    }

    $script:Passed++
}

$repositoryRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent

$appSetupPath = Join-Path $repositoryRoot 'Scripts\ApplicationSetupScripts\Set-GraphCredentialsAzureADApplication.ps1'
$appSetup = [IO.File]::ReadAllText($appSetupPath)
$applicationPermissionNamesBlock = [regex]::Match(
    $appSetup,
    '(?s)\$applicationPermissionNames\s*=\s*@\((?<Roles>.*?)\)\s*if\s*\(\$EnableDemoAgentPermissions\)')
Assert-True $applicationPermissionNamesBlock.Success 'the Graph application setup exposes its baseline application-permission names'
Assert-RoleSet `
    -ActualRoles (Get-RoleNames -Text $applicationPermissionNamesBlock.Groups['Roles'].Value) `
    -Because 'the Graph application setup requests the five-role baseline'

$appPermissionBlock = [regex]::Match(
    $appSetup,
    '(?s)\$appPermissions\s*=.*?(?=\$delegatedPermissions)')
Assert-True $appPermissionBlock.Success 'the Graph application setup exposes its application-permission block'
Assert-True (
    $appPermissionBlock.Value -match '\$_\.Value\s+-in\s+\$applicationPermissionNames'
) 'the Graph application setup resolves the declared permission names'
$demoPermissionNamesBlock = [regex]::Match(
    $appSetup,
    '(?s)if\s*\(\$EnableDemoAgentPermissions\)\s*\{\s*\$applicationPermissionNames\s*\+=\s*@\((?<Roles>.*?)\)\s*\}')
Assert-True $demoPermissionNamesBlock.Success 'the Graph application setup declares its optional demo-agent permissions'
Assert-DemoRoleSet `
    -ActualRoles (Get-DemoRoleNames -Text $demoPermissionNamesBlock.Groups['Roles'].Value) `
    -Because 'the demo-agent switch adds the documented seven optional application roles'
Assert-True (
    $appSetup -match '(?s)\.EXAMPLE.*?Set-GraphCredentialsAzureADApplication.*?-AppTenantId.*?-KeyVaultTenantId'
) 'the Graph application setup example uses its executable parameter names'

$managedIdentityPath = Join-Path $repositoryRoot 'Scripts\PostDeploymentRoleAssignments\Set-UserManagedIdentityPermissions.ps1'
$managedIdentityScript = [IO.File]::ReadAllText($managedIdentityPath)
$managedIdentityRoleLists = [regex]::Matches(
    $managedIdentityScript,
    '(?s)\$appRoles\s*=\s*@\((?<Roles>.*?)\)')
Assert-True ($managedIdentityRoleLists.Count -eq 2) 'the managed-identity script has one instruction list and one assignment list'
foreach ($roleList in $managedIdentityRoleLists) {
    Assert-RoleSet `
        -ActualRoles (Get-RoleNames -Text $roleList.Groups['Roles'].Value) `
        -Because 'each managed-identity role list uses the five-role baseline'
}
Assert-True (
    $managedIdentityScript -match '(?s)\.EXAMPLE.*?Set-UserManagedIdentityPermissions.*?-TenantId'
) 'the managed-identity script example supplies every mandatory parameter'

$documentationPaths = @(
    'Deployment\Documentation\Deploying_GMM.md',
    'Scripts\ApplicationSetupScripts\Manual Setup Documentation\GraphCredentials-Application-Creation-Instructions.md',
    'faq.md'
)

$documentationContent = @{}
foreach ($relativePath in $documentationPaths) {
    $path = Join-Path $repositoryRoot $relativePath
    $content = [IO.File]::ReadAllText($path)
    $documentationContent[$relativePath] = $content
    $baselineLines = @(
        $content -split '\r?\n' |
            Where-Object { $_ -match '(?i)application-role baseline' }
    )

    Assert-True ($baselineLines.Count -ge 1) "$relativePath states the application-role baseline"
    foreach ($baselineLine in $baselineLines) {
        Assert-RoleSet `
            -ActualRoles (Get-RoleNames -Text $baselineLine) `
            -Because "$relativePath keeps its stated baseline at exactly five roles"
    }
}

$operatorRoleSections = @(
    @{
        Path = 'Deployment\Documentation\Deploying_GMM.md'
        Pattern = '(?s)#### App Registrations and Their API Permissions(?<Roles>.*?)#### Managed Identity Permissions'
        Name = 'the deployment Graph-app permission table'
    },
    @{
        Path = 'Deployment\Documentation\Deploying_GMM.md'
        Pattern = '(?s)#### Managed Identity Permissions(?<Roles>.*?)\r?\n---'
        Name = 'the deployment managed-identity permission list'
    },
    @{
        Path = 'Scripts\ApplicationSetupScripts\Manual Setup Documentation\GraphCredentials-Application-Creation-Instructions.md'
        Pattern = '(?s)#### 5\.1 Add Microsoft Graph Application Permissions(?<Roles>.*?)#### 5\.2'
        Name = 'the manual Graph-app permission list'
    }
)

foreach ($section in $operatorRoleSections) {
    $match = [regex]::Match(
        $documentationContent[$section.Path],
        $section.Pattern)
    Assert-True $match.Success "$($section.Name) is present"
    Assert-RoleSet `
        -ActualRoles (Get-RoleNames -Text $match.Groups['Roles'].Value) `
        -Because "$($section.Name) contains the complete five-role baseline"
}

foreach ($relativePath in $documentationPaths) {
    Assert-True (
        @(Get-DemoRoleNames -Text $documentationContent[$relativePath]).Count -eq 0
    ) "$relativePath does not include demo-agent role instructions"
    Assert-True (
        $documentationContent[$relativePath] -notmatch '(?i)EnableDemoAgentPermissions|demo-agent'
    ) "$relativePath does not direct operators to demo-agent setup"
}

Write-Host ("{0} Graph permission baseline checks passed." -f $script:Passed) -ForegroundColor Green
