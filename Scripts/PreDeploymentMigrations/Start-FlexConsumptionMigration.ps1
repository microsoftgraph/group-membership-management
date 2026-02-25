$ErrorActionPreference = "Stop"

# Global variable to control Azure cmdlet warning suppression
# Set to $true to hide Azure PowerShell warnings, $false to show them
$Global:SuppressAzureWarnings = $false

if ($Global:SuppressAzureWarnings) {
    $WarningPreference = "SilentlyContinue"
}

$ScriptsDirectory = Split-Path $PSScriptRoot -Parent
. ($ScriptsDirectory + '/ReusableModules/Invoke-WithRetry.ps1')

<#
.SYNOPSIS
Automatically detects and migrates Azure Functions from Consumption (Y1) to Flex Consumption (FC1) plans.

.DESCRIPTION
This script analyzes bicep templates to determine the desired SKU configuration and compares it with
the current Azure deployment. When functions need to be migrated from Consumption to Flex Consumption,
it safely removes the existing resources and their SQL permissions, allowing the deployment to create
new resources with the correct SKU.

.PARAMETER FunctionTemplatesPath
Path to the functions ARM templates directory (e.g., "<path-to>/functions_arm_templates")

.PARAMETER ComputeResourcesArmTemplatePath
Path to the computeResources.json arm template leveraged by the deployment package (e.g., "<path-to>/computeResources.json")

.PARAMETER SolutionAbbreviation
Abbreviation used to denote the overall solution (e.g., "gmm")

.PARAMETER EnvironmentAbbreviation
Abbreviation for the environment (e.g., "dev", "prod")

.PARAMETER SyncJobsDBConnectionString
Connection string for SyncJobsDB database (sqlDatabaseConnectionString from KeyVault)

.PARAMETER ADFDBConnectionString
Connection string for ADF database (sqlServerBasicConnectionString from KeyVault)

.PARAMETER WhatIf
SIMULATION MODE: Shows which functions would be migrated and what actions would be taken,
without making any actual changes. Use this to safely preview the migration plan.

.PARAMETER SkipPreCheck
Skip the pre-check that tests if migration is needed. Use when you already know migration is required.

.PARAMETER SkipFirewallCheck
Skip automatic SQL Server firewall rule management. Use if your IP is already whitelisted or you're handling it manually.

.PARAMETER SkipConfirmation
Skip confirmation prompts and proceed automatically. Use for unattended/automated deployments.

.EXAMPLE
Start-FlexConsumptionMigration -FunctionTemplatesPath "<path-to-functions_arm_templates>" -SolutionAbbreviation "gmm" -EnvironmentAbbreviation "dev" -WhatIf

.EXAMPLE
Start-FlexConsumptionMigration -FunctionTemplatesPath "<path-to-functions_arm_templates>" -SolutionAbbreviation "gmm" -EnvironmentAbbreviation "dev" -SyncJobsDBConnectionString $conn1 -ADFDBConnectionString $conn2 -SkipConfirmation

.EXAMPLE
Start-FlexConsumptionMigration -FunctionTemplatesPath "<path-to-functions_arm_templates>" -SolutionAbbreviation "gmm" -EnvironmentAbbreviation "dev" -SyncJobsDBConnectionString $syncJobsConnectionString -ADFDBConnectionString $adfConnectionString

.EXAMPLE
Start-FlexConsumptionMigration -ComputeResourcesArmTemplatePath "<path-to>/computeResources.json" -SolutionAbbreviation "gmm" -EnvironmentAbbreviation "dev" -WhatIf

.EXAMPLE
Start-FlexConsumptionMigration -ComputeResourcesArmTemplatePath "<path-to>/computeResources.json" -SolutionAbbreviation "gmm" -EnvironmentAbbreviation "dev" -SyncJobsDBConnectionString $conn1 -ADFDBConnectionString $conn2 -SkipConfirmation

.EXAMPLE
Start-FlexConsumptionMigration -ComputeResourcesArmTemplatePath "<path-to>/computeResources.json" -SolutionAbbreviation "gmm" -EnvironmentAbbreviation "dev" -SyncJobsDBConnectionString $syncJobsConnectionString -ADFDBConnectionString $adfConnectionString
#>

function Get-WarningAction {
    # Helper function to get warning action based on global setting
    if ($Global:SuppressAzureWarnings) {
        return "SilentlyContinue"
    } else {
        return "Continue"
    }
}

function Invoke-SqlNonQuery {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ConnectionString,
        [Parameter(Mandatory = $true)]
        [string]$Query,
        [int]$CommandTimeoutSeconds = 30
    )
    $connection = $null
    $command = $null
    $handler = $null
    $messages = New-Object System.Collections.Generic.List[string]
    try {
        $connection = New-Object System.Data.SqlClient.SqlConnection $ConnectionString
        # Capture PRINT and low-severity messages from SQL
        $connection.FireInfoMessageEventOnUserErrors = $true
        $handler = [System.Data.SqlClient.SqlInfoMessageEventHandler] {
            param($sender, $eventArgs)
            foreach ($err in $eventArgs.Errors) { $messages.Add($err.Message) }
        }
        $connection.add_InfoMessage($handler)

        $context = [Microsoft.Azure.Commands.Common.Authentication.Abstractions.AzureRmProfileProvider]::Instance.Profile.DefaultContext
        $sqlToken = [Microsoft.Azure.Commands.Common.Authentication.AzureSession]::Instance.AuthenticationFactory.Authenticate($context.Account, $context.Environment, $context.Tenant.Id.ToString(), $null, [Microsoft.Azure.Commands.Common.Authentication.ShowDialog]::Never, $null, "https://database.windows.net").AccessToken
        $connection.AccessToken = $sqlToken
        $connection.Open()

        $command = $connection.CreateCommand()
        $command.CommandText = $Query
        $command.CommandTimeout = $CommandTimeoutSeconds
        [void]$command.ExecuteNonQuery()

        if ($messages.Count -gt 0) {
            foreach ($m in $messages) {
                Write-Host "    ℹ️  SQL: $m" -ForegroundColor DarkGray
            }
        }
    }
    catch [System.Data.SqlClient.SqlException] {
        # Provide rich SQL error details before bubbling up
        $sqlEx = $_.Exception
        foreach ($e in $sqlEx.Errors) {
            Write-Warning ("    SQL {0}: {1} (State {2}, Line {3})" -f $e.Number, $e.Message, $e.State, $e.LineNumber)
        }
        throw
    }
    finally {
        if ($connection -and $handler) { $connection.remove_InfoMessage($handler) }
        if ($null -ne $command) { $command.Dispose() }
        if ($null -ne $connection) { $connection.Dispose() }
    }
}

function Show-MigrationSummary {
    param(
        [array]$MigrationsNeeded,
        [string]$SolutionAbbreviation,
        [string]$EnvironmentAbbreviation,
        [switch]$Detailed
    )
    if ($Detailed) {
        Write-Host ""
        Write-Host "📋 The following actions WOULD be performed:" -ForegroundColor Cyan
        foreach ($migration in $MigrationsNeeded) {
            Write-Host ""
            Write-Host "  🔧 Function: $($migration.FunctionType)" -ForegroundColor Yellow
            Write-Host "     ├─ Function App: $($migration.FunctionName)" -ForegroundColor White
            Write-Host "     ├─ Service Plan: $($migration.ServicePlanName)" -ForegroundColor White
            Write-Host "     ├─ Current SKU: $($migration.CurrentSku)/$($migration.CurrentTier)" -ForegroundColor Red
            Write-Host "     ├─ Target SKU: $($migration.DesiredSku)/$($migration.DesiredTier)" -ForegroundColor Green
            Write-Host "     ├─ SQL User: $($migration.ManagedIdentityName)" -ForegroundColor White
            $sqlActions = "DROP SQL user from $SolutionAbbreviation-data-$EnvironmentAbbreviation, DROP SQL user from $SolutionAbbreviation-data-$EnvironmentAbbreviation-adf"
            Write-Host "     └─ Actions: DELETE function app, DELETE service plan, $sqlActions" -ForegroundColor Red
        }
    }
    else {
        Write-Host ""
        Write-Host "📊 Migration Summary:" -ForegroundColor Cyan
        Write-Host "   Functions to migrate: $($MigrationsNeeded.Count)" -ForegroundColor Yellow
        $MigrationsNeeded | ForEach-Object {
            Write-Host "   • $($_.FunctionType): $($_.CurrentSku)/$($_.CurrentTier) → $($_.DesiredSku)/$($_.DesiredTier)" -ForegroundColor White
        }
    }
}

function Get-SkuFromBicep {
    param([string]$BicepContent)
    # Multiple robust patterns for SKU detection
    $skuPatterns = @(
        "param\s+(?:servicePlan)?[Ss]ku\s+string\s*=\s*[`"']?(Y1|FC1)[`"']?",
        "param\s+(?:servicePlan)?[Ss]ku\s+string\s*=\s*(?://.*?)?\r?\n?\s*[`"']?(Y1|FC1)[`"']?",
        "@allowed\s*\(\s*\[[\s\S]*?[`"']?(Y1|FC1)[`"']?[\s\S]*?\]\s*\)",
        "default\s*[Vv]alue\s*[:=]\s*[`"']?(Y1|FC1)[`"']?"
    )
    foreach ($pattern in $skuPatterns) {
        if ($BicepContent -match $pattern) {
            return $matches[1]
        }
    }
    return "Y1" # Safe default
}

function Get-TierFromBicep {
    param([string]$BicepContent)
    # Robust tier detection patterns
    $tierPatterns = @(
        "tier\s*:\s*[`"']?(Dynamic|FlexConsumption)[`"']?",
        "tier\s*=\s*[`"']?(Dynamic|FlexConsumption)[`"']?",
        "[`"']tier[`"']\s*:\s*[`"']?(Dynamic|FlexConsumption)[`"']?",
        "tier\s*:\s*\r?\n?\s*[`"']?(Dynamic|FlexConsumption)[`"']?",
        "sku\s*:\s*\{\s*[\s\S]*?tier\s*:\s*[`"']?(Dynamic|FlexConsumption)[`"']?[\s\S]*?\}",
        "//.*?(Dynamic|FlexConsumption)",
        "\/\*[\s\S]*?(Dynamic|FlexConsumption)[\s\S]*?\*\/"
    )
    foreach ($pattern in $tierPatterns) {
        if ($BicepContent -match $pattern) {
            return $matches[1]
        }
    }
    # Infer from SKU if not found
    $sku = Get-SkuFromBicep -BicepContent $BicepContent
    return if ($sku -eq "FC1") { "FlexConsumption" } else { "Dynamic" }
}

function Get-FunctionsFromComputeArmJson {
    param(
        [string]$ComputeResourcesArmTemplatePath
    )

    if (-not (Test-Path $ComputeResourcesArmTemplatePath)) {
        throw "Compute resources ARM template path not found: $ComputeResourcesArmTemplatePath"
    }

    # Load the JSON ARM template
    $template = Get-Content -Path $ComputeResourcesArmTemplatePath -Raw | ConvertFrom-Json

    $functions = @()

    # Pattern 1: Regular compute resource templates (e.g., "jobTriggerComputeResourcesTemplate")
    $computeResourceTemplates = $template.resources | Where-Object { $_.name -like "*ComputeResourcesTemplate" }
    $servicePlanTemplates = $computeResourceTemplates | ForEach-Object { $_.properties.template.resources | Where-Object { $_.name -like "servicePlanTemplate*" } }

    foreach ($servicePlanTemplate in $servicePlanTemplates) {
        $functionName = ($servicePlanTemplate.name -split "-")[-1]
        $skuName = $servicePlanTemplate.properties.template.parameters.sku.defaultValue
        $servicePlan = $servicePlanTemplate.properties.template.resources | Where-Object { $_.type -eq "Microsoft.Web/serverfarms" } | Select-Object -First 1

        $functions += [PSCustomObject]@{
            FunctionName = $functionName
            Sku  = $skuName
            Tier = $servicePlan.sku.tier
        }
    }

    # Pattern 2: Copy loop deployments (e.g., "messageSplitter{0}ComputeResources", "graphUpdater{0}ComputeResourcesTemplate")
    # These have nested templates with servicePlanTemplate resources inside
    $copyLoopTemplates = $template.resources | Where-Object {
        $null -ne $_.copy -and ($_.name -like "*ComputeResources*")
    }

    foreach ($copyLoopTemplate in $copyLoopTemplates) {
        # Extract base function name from the copy loop name pattern
        # e.g., "[format('messageSplitter{0}ComputeResources', variables('instanceIds')[copyIndex()])]" -> "MessageSplitter"
        $templateName = $copyLoopTemplate.name
        $baseFunctionName = $null

        if ($templateName -match "format\('([a-zA-Z]+)\{0\}ComputeResources") {
            $baseFunctionName = $Matches[1]
            # Capitalize first letter for consistency
            $baseFunctionName = $baseFunctionName.Substring(0,1).ToUpper() + $baseFunctionName.Substring(1)
        }
        elseif ($templateName -match "'([a-zA-Z]+)ComputeResourcesTemplate'") {
            $baseFunctionName = $Matches[1]
            $baseFunctionName = $baseFunctionName.Substring(0,1).ToUpper() + $baseFunctionName.Substring(1)
        }

        if ($null -eq $baseFunctionName) {
            Write-Verbose "Could not extract function name from copy loop template: $templateName"
            continue
        }

        # Find servicePlanTemplate inside the nested template resources
        $nestedResources = $copyLoopTemplate.properties.template.resources
        $nestedServicePlanTemplates = $nestedResources | Where-Object { $_.name -like "*servicePlanTemplate*" }

        foreach ($servicePlanTemplate in $nestedServicePlanTemplates) {
            # Get SKU from the nested template parameters
            $skuName = $null
            if ($servicePlanTemplate.properties.template.parameters.sku) {
                $skuName = $servicePlanTemplate.properties.template.parameters.sku.defaultValue
            }

            # Get tier from the service farm resource in the nested template
            $servicePlan = $servicePlanTemplate.properties.template.resources | Where-Object { $_.type -eq "Microsoft.Web/serverfarms" } | Select-Object -First 1
            $tier = if ($servicePlan -and $servicePlan.sku) { $servicePlan.sku.tier } else { $null }

            # Also check the outer template parameters for SKU default value
            if ([string]::IsNullOrEmpty($skuName)) {
                $outerParams = $copyLoopTemplate.properties.template.parameters
                if ($outerParams.servicePlanSku -and $outerParams.servicePlanSku.defaultValue) {
                    $skuName = $outerParams.servicePlanSku.defaultValue
                }
            }

            # Infer tier from SKU if not found
            if ([string]::IsNullOrEmpty($tier) -or $tier -eq "Dynamic") {
                $tier = if ($skuName -eq "FC1") { "FlexConsumption" } else { "Dynamic" }
            }

            Write-Host "  📋 $baseFunctionName (copy loop): SKU=$skuName, Tier=$tier" -ForegroundColor Gray

            $functions += [PSCustomObject]@{
                FunctionName = $baseFunctionName
                Sku  = $skuName
                Tier = $tier
            }

            # Only add once per function type (not per instance)
            break
        }
    }

    return $functions
}

function Get-FunctionsFromBicep {
    param(
        [string]$FunctionTemplatesPath
    )

    if (-not (Test-Path $FunctionTemplatesPath)) {
        throw "Function templates path not found: $FunctionTemplatesPath"
    }

    $functions = @()

    $functionFolders = Get-ChildItem -Path "$FunctionTemplatesPath" -Directory

    foreach ($folder in $functionFolders) {
        $servicePlanPath = Join-Path $folder.FullName "Infrastructure/compute/servicePlan.bicep"
        if (Test-Path $servicePlanPath) {
            try {
                $servicePlanContent = Get-Content $servicePlanPath -Raw
                $desiredSku  = Get-SkuFromBicep  -BicepContent $servicePlanContent
                $desiredTier = Get-TierFromBicep -BicepContent $servicePlanContent

                Write-Host "  📋 $($folder.Name): SKU=$desiredSku, Tier=$desiredTier" -ForegroundColor Gray

                # Create object and add to functions list
                $functions += [PSCustomObject]@{
                    FunctionName        = $folder.Name
                    Sku                 = $desiredSku
                    Tier                = $desiredTier
                }
            }
            catch {
                Write-Warning "Failed to parse bicep files for '$($folder.Name)': $($_.Exception.Message)"
            }
        }
        else {
            Write-Host "Skipping '$($folder.Name)': No servicePlan.bicep found"
        }
    }

    return $functions
}

function Get-FunctionsRequiringMigration {
    param(
        [string]$FunctionTemplatesPath,
        [string]$ComputeResourcesArmTemplatePath,
        [string]$ResourceGroupName,
        [string]$SolutionAbbreviation,
        [string]$EnvironmentAbbreviation
    )

    $functionsToMigrate = @()

    if (-not [string]::IsNullOrEmpty($FunctionTemplatesPath)) {
        $desiredFunctions = Get-FunctionsFromBicep -FunctionTemplatesPath $FunctionTemplatesPath

        if ($null -eq $desiredFunctions -or $desiredFunctions.Count -eq 0) {
            Write-Host "No function definitions found in templates at '$FunctionTemplatesPath'" -ForegroundColor Yellow
            return @()  # Return empty array - no migrations needed
        }
    }
    elseif (-not [string]::IsNullOrEmpty($ComputeResourcesArmTemplatePath)) {
        $desiredFunctions = Get-FunctionsFromComputeArmJson -ComputeResourcesArmTemplatePath $ComputeResourcesArmTemplatePath
        write-Host "  📋 Detected $($desiredFunctions.Count) functions from computeResources.json" -ForegroundColor Gray

        if ($null -eq $desiredFunctions -or $desiredFunctions.Count -eq 0) {
            Write-Host "No function definitions found in template at '$ComputeResourcesArmTemplatePath'" -ForegroundColor Yellow
            return @()  # Return empty array - no migrations needed
        }
    }

    Write-Host "🔍 Analyzing $($desiredFunctions.Count) functions from templates..." -ForegroundColor Cyan

    # Set warning action once
    $warningAction = Get-WarningAction

    # Query Azure once and cache the results
    Write-Host "  ☁️  Querying Azure Function Apps..." -ForegroundColor Gray
    $allFunctionApps = Get-AzFunctionApp -ResourceGroupName $ResourceGroupName -ErrorAction SilentlyContinue -WarningAction $warningAction

    # Check if any function apps exist
    if ($null -eq $allFunctionApps -or $allFunctionApps.Count -eq 0) {
        Write-Host "  📊 No function apps found in resource group '$ResourceGroupName'" -ForegroundColor Yellow
        return @()  # Return empty array - no migrations needed
    }

    Write-Host "  ☁️  Querying Azure Service Plans..." -ForegroundColor Gray
    $allServicePlans = Get-AzAppServicePlan -ResourceGroupName $ResourceGroupName -ErrorAction SilentlyContinue -WarningAction $warningAction

    # Create hashtable for fast service plan lookups
    $servicePlanLookup = @{}
    if ($null -ne $allServicePlans) {
        foreach ($plan in $allServicePlans) {
            $servicePlanLookup[$plan.Name] = $plan
        }
    }

    Write-Host "  📊 Found $($allFunctionApps.Count) function apps and $(if ($null -eq $allServicePlans) { 0 } else { $allServicePlans.Count }) service plans" -ForegroundColor Gray
    foreach ($desiredFunction in $desiredFunctions) {
        $desiredSku = $desiredFunction.Sku
        $desiredTier = $desiredFunction.Tier

        $existingFunctions = $allFunctionApps | Where-Object {
            $_.Name -like "*$($desiredFunction.FunctionName)*" -or $_.Name -like "*$($desiredFunction.FunctionName.ToLower())*"
        }

        foreach ($existingFunction in $existingFunctions) {
            try {
                # Use cached service plan data
                $servicePlanName = $existingFunction.ServerFarmId.Split('/')[-1]
                $servicePlan = $servicePlanLookup[$servicePlanName]

                if (-not $servicePlan) {
                    Write-Warning "Service plan '$servicePlanName' not found for function '$($existingFunction.Name)'"
                    continue
                }

                $currentSku = $servicePlan.Sku.Name
                $currentTier = switch ($servicePlan.Sku.Tier) {
                    "Dynamic" { "Dynamic" }
                    "FlexConsumption" { "FlexConsumption" }
                    default { "Dynamic" }
                }
                # Migration logic with exact matching
                $needsMigration = (
                    ($currentSku -eq "Y1" -and $desiredSku -eq "FC1") -or
                    ($currentTier -eq "Dynamic" -and $desiredTier -eq "FlexConsumption") -or
                    ($currentSku -ne $desiredSku) -or
                    ($currentTier -ne $desiredTier)
                )
                if ($needsMigration) {
                    $functionsToMigrate += @{
                        FunctionName = $existingFunction.Name
                        FunctionType = $desiredFunction.FunctionName
                        ServicePlanName = $servicePlan.Name
                        CurrentSku = $currentSku
                        CurrentTier = $currentTier
                        DesiredSku = $desiredSku
                        DesiredTier = $desiredTier
                        ManagedIdentityName = $existingFunction.Name
                    }
                    Write-Host "  ⚠️  $($existingFunction.Name): $currentSku/$currentTier → $desiredSku/$desiredTier" -ForegroundColor Yellow
                }
                else {
                    Write-Host "  ✅ $($existingFunction.Name): Already matches desired configuration" -ForegroundColor Green
                }
            }
            catch {
                Write-Warning "Failed to analyze function '$($existingFunction.Name)': $($_.Exception.Message)"
            }
        }
    }
    return $functionsToMigrate
}

function Remove-FunctionAppAndServicePlan {
    param(
        [string]$FunctionName,
        [string]$ServicePlanName,
        [string]$ResourceGroupName,
        [switch]$WhatIf
    )
    if ($WhatIf) {
        Write-Host "    [WhatIf] Would remove function app: $FunctionName" -ForegroundColor Magenta
        Write-Host "    [WhatIf] Would remove service plan: $ServicePlanName" -ForegroundColor Magenta
        return
    }
    try {
        Write-Host "    🗑️  Removing function app: $FunctionName..." -ForegroundColor Yellow
        Invoke-WithRetry -OperationName "Remove function app '$FunctionName'" -Operation {
            Remove-AzFunctionApp -ResourceGroupName $ResourceGroupName -Name $FunctionName -Force -ErrorAction Stop
        }
        Write-Host "    🗑️  Removing service plan: $ServicePlanName..." -ForegroundColor Yellow
        Invoke-WithRetry -OperationName "Remove service plan '$ServicePlanName'" -Operation {
            Remove-AzAppServicePlan -ResourceGroupName $ResourceGroupName -Name $ServicePlanName -Force -ErrorAction Stop
        }
        Write-Host "    ✅ Successfully removed resources" -ForegroundColor Green
    }
    catch {
        Write-Error "Failed to remove resources: $($_.Exception.Message)"
        throw
    }
}

function Set-UpdateSqlServerFirewallRule {
    param(
        [string]$ResourceGroupName,
        [string]$SqlServerName
    )
    # Get current public IP
    Write-Host "    🔍 Detecting current public IP address..." -ForegroundColor Gray
    $publicIp = $null
    try {
        $publicIp = (Invoke-RestMethod -Uri "https://api.ipify.org?format=text" -TimeoutSec 5)
    }
    catch {
        try {
            # Fallback to another service
            $publicIp = (Invoke-RestMethod -Uri "https://ifconfig.me/ip" -TimeoutSec 5)
        }
        catch {
            Write-Warning "Could not detect public IP address. SQL operations may fail if your IP is not whitelisted."
            return $false
        }
    }
    if ($publicIp) {
        Write-Host "    📍 Current public IP: $publicIp" -ForegroundColor Gray
        # Check if this IP is already whitelisted
        $existingRule = Get-AzSqlServerFirewallRule -ResourceGroupName $ResourceGroupName -ServerName $SqlServerName -ErrorAction SilentlyContinue |
            Where-Object { $_.StartIpAddress -eq $publicIp -and $_.EndIpAddress -eq $publicIp }
        if ($existingRule) {
            Write-Host "    ✅ IP already whitelisted in SQL firewall (Rule: $($existingRule.FirewallRuleName))" -ForegroundColor Green
            return $true
        }
        # Create a rule name that includes the IP address for easy identification
        # Replace dots with dashes for valid Azure resource naming
        $ipForRuleName = $publicIp -replace '\.', '-'
        $ruleName = "FlexMigration-$ipForRuleName"
        # Add firewall rule for this IP
        try {
            Write-Host "    🔥 Adding SQL firewall rule for this location..." -ForegroundColor Yellow
            Invoke-WithRetry -OperationName "Add SQL firewall rule '$ruleName'" -Operation {
                New-AzSqlServerFirewallRule -ResourceGroupName $ResourceGroupName -ServerName $SqlServerName `
                    -FirewallRuleName $ruleName -StartIpAddress $publicIp -EndIpAddress $publicIp -ErrorAction Stop | Out-Null
            }
            Write-Host "    ✅ Firewall rule '$ruleName' added successfully" -ForegroundColor Green
            Write-Host "    💡 This rule will persist for future migrations from this location" -ForegroundColor Gray
            return $true
        }
        catch {
            # Check if it's because the rule name already exists (shouldn't happen, but just in case)
            if ($_.Exception.Message -like "*already exists*") {
                Write-Host "    ✅ Firewall rule for this IP already exists" -ForegroundColor Green
                return $true
            }
            Write-Warning "Failed to add SQL firewall rule: $($_.Exception.Message)"
            Write-Host "    ⚠️  You may need to manually add your IP ($publicIp) to the SQL Server firewall" -ForegroundColor Yellow
            return $false
        }
    }
    return $false
}

function Remove-SqlDatabasePermissions {
    param(
        [string]$IdentityName,
        [string]$SyncJobsDBConnectionString,
        [string]$ADFDBConnectionString,
        [string]$SolutionAbbreviation,
        [string]$EnvironmentAbbreviation,
        [switch]$WhatIf
    )
    if ($WhatIf) {
        Write-Host "    [WhatIf] Would drop SQL user from $SolutionAbbreviation-data-$EnvironmentAbbreviation DB: $IdentityName" -ForegroundColor Magenta
        Write-Host "    [WhatIf] Would drop SQL user from $SolutionAbbreviation-data-$EnvironmentAbbreviation-adf DB: $IdentityName" -ForegroundColor Magenta
        return
    }

    # Drop user from SyncJobsDB (all functions)
    if (-not [string]::IsNullOrEmpty($SyncJobsDBConnectionString)) {
        try {
            Write-Host "    🗄️  Removing $SolutionAbbreviation-data-$EnvironmentAbbreviation DB permissions for: $IdentityName..." -ForegroundColor Yellow
            $dropUserQuery = @"
            IF EXISTS (SELECT * FROM sys.database_principals WHERE name = '$IdentityName')
            BEGIN
                DROP USER [$IdentityName]
                PRINT 'Dropped user from $SolutionAbbreviation-data-$EnvironmentAbbreviation DB: $IdentityName'
            END
            ELSE
            BEGIN
                PRINT 'User not found in $SolutionAbbreviation-data-$EnvironmentAbbreviation DB: $IdentityName'
            END
"@
            Invoke-SqlNonQuery -ConnectionString $SyncJobsDBConnectionString -Query $dropUserQuery
            Write-Host "    ✅ Successfully removed $SolutionAbbreviation-data-$EnvironmentAbbreviation DB permissions" -ForegroundColor Green
        }
        catch {
            Write-Warning "Failed to remove $SolutionAbbreviation-data-$EnvironmentAbbreviation DB permissions for '$IdentityName': $($_.Exception.Message)"
        }
    }
    else {
        Write-Warning "No $SolutionAbbreviation-data-$EnvironmentAbbreviation DB connection string provided, skipping permissions cleanup"
    }

    # Drop user from ADFDB (all functions - some may not exist, that's OK)
    if (-not [string]::IsNullOrEmpty($ADFDBConnectionString)) {
        try {
            Write-Host "    🗄️  Removing $SolutionAbbreviation-data-$EnvironmentAbbreviation-adf DB permissions for: $IdentityName..." -ForegroundColor Yellow
            $dropUserQuery = @"
            IF EXISTS (SELECT * FROM sys.database_principals WHERE name = '$IdentityName')
            BEGIN
                DROP USER [$IdentityName]
                PRINT 'Dropped user from $SolutionAbbreviation-data-$EnvironmentAbbreviation-adf DB: $IdentityName'
            END
            ELSE
            BEGIN
                PRINT 'User not found in $SolutionAbbreviation-data-$EnvironmentAbbreviation-adf DB: $IdentityName'
            END
"@
            Invoke-SqlNonQuery -ConnectionString $ADFDBConnectionString -Query $dropUserQuery
            Write-Host "    ✅ Successfully removed $SolutionAbbreviation-data-$EnvironmentAbbreviation-adf DB permissions" -ForegroundColor Green
        }
        catch {
            Write-Warning "Failed to remove $SolutionAbbreviation-data-$EnvironmentAbbreviation-adf DB permissions for '$IdentityName': $($_.Exception.Message)"
        }
    }
    else {
        Write-Warning "No $SolutionAbbreviation-data-$EnvironmentAbbreviation-adf DB connection string provided, skipping permissions cleanup"
    }
}

function Test-FlexConsumptionMigrationNeeded {
    <#
    .SYNOPSIS
    Tests if any Azure Functions need to be migrated from Consumption to Flex Consumption plans.

    .DESCRIPTION
    This function checks if migration is needed without requiring SQL connection strings.
    It returns an array of functions needing migration (empty if none).

    .PARAMETER FunctionTemplatesPath
    Path to the functions ARM templates directory

    .PARAMETER SolutionAbbreviation
    Abbreviation used to denote the overall solution (e.g., "gmm")

    .PARAMETER EnvironmentAbbreviation
    Abbreviation for the environment (e.g., "dev", "prod")

    .EXAMPLE
    $migrations = Test-FlexConsumptionMigrationNeeded -FunctionTemplatesPath "path" -SolutionAbbreviation "gmm" -EnvironmentAbbreviation "dev"
    if ($migrations.Count -gt 0) {
        # Migration needed - get connection strings and run migration
    }
    #>
    [CmdletBinding()]
    param(
        [string]$FunctionTemplatesPath,
        [string]$ComputeResourcesArmTemplatePath,
        [string]$SolutionAbbreviation,
        [string]$EnvironmentAbbreviation
    )
    Write-Host "🔍 Checking if Flex Consumption migration is needed..." -ForegroundColor Cyan
    # Set warning action once
    $warningAction = Get-WarningAction
    # Verify Azure context
    $context = Get-AzContext -WarningAction $warningAction
    if (-not $context) {
        Write-Host "❌ No Azure context found" -ForegroundColor Red
        Write-Host "   Please run Connect-AzAccount first to authenticate with Azure." -ForegroundColor Yellow
        return @()
    }
    # Determine resource group name
    $computeResourceGroupName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation"
    # Check if resource group exists
    $resourceGroup = Get-AzResourceGroup -Name $computeResourceGroupName -ErrorAction SilentlyContinue -WarningAction $warningAction
    if (-not $resourceGroup) {
        Write-Host "✅ No Azure resources exist yet - nothing to migrate!" -ForegroundColor Green
        return @()
    }
    # Check for functions requiring migration
    $migrationsNeeded = Get-FunctionsRequiringMigration -FunctionTemplatesPath $FunctionTemplatesPath -ComputeResourcesArmTemplatePath $ComputeResourcesArmTemplatePath -ResourceGroupName $computeResourceGroupName -SolutionAbbreviation $SolutionAbbreviation -EnvironmentAbbreviation $EnvironmentAbbreviation

    if ($migrationsNeeded.Count -eq 0) {
        Write-Host "✅ No migration needed!" -ForegroundColor Green
        return @()
    }
    Write-Host "⚠️  Found $($migrationsNeeded.Count) function(s) requiring migration to Flex Consumption" -ForegroundColor Yellow
    $migrationsNeeded | ForEach-Object {
        Write-Host "   • $($_.FunctionType): $($_.CurrentSku)/$($_.CurrentTier) → $($_.DesiredSku)/$($_.DesiredTier)" -ForegroundColor White
    }
    # Return the actual migration data to avoid re-querying
    return $migrationsNeeded
}

function Start-FlexConsumptionMigration {
    [CmdletBinding(DefaultParameterSetName="RepositoryBicep")]
    param(
        # parameter set for leverating the bicep files in the repository
        [Parameter(ParameterSetName="RepositoryBicep", Mandatory)]
        [string]$FunctionTemplatesPath,

        # parameter set for leveraging the computeResources.json file in the deployment package
        [Parameter(ParameterSetName="DeploymentPackageArmJson", Mandatory)]
        [string]$ComputeResourcesArmTemplatePath,

        # common parameters
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $false)]
        [string]$SyncJobsDBConnectionString,
        [Parameter(Mandatory = $false)]
        [string]$ADFDBConnectionString,
        [Parameter(Mandatory = $false)]
        [switch]$WhatIf,
        [Parameter(Mandatory = $false)]
        [switch]$SkipPreCheck,
        [Parameter(Mandatory = $false)]
        [switch]$SkipFirewallCheck,
        [Parameter(Mandatory = $false)]
        [switch]$SkipConfirmation
    )

    $startTime = Get-Date
    # Determine resource group name once
    $computeResourceGroupName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation"
    # Get migration data (handles all checks internally)
    $migrationsNeeded = @()
    if (-not $SkipPreCheck) {
        $migrationsNeeded = Test-FlexConsumptionMigrationNeeded -FunctionTemplatesPath $FunctionTemplatesPath -ComputeResourcesArmTemplatePath $ComputeResourcesArmTemplatePath -SolutionAbbreviation $SolutionAbbreviation -EnvironmentAbbreviation $EnvironmentAbbreviation
        if ($migrationsNeeded.Count -eq 0) {
            Write-Host ""
            Write-Host "✅ Migration check complete - no action needed" -ForegroundColor Green
            return
        }
        Write-Host ""
    }
    else {
        # If skipping pre-check, we need to verify Azure context and get migrations
        $warningAction = Get-WarningAction
        $context = Get-AzContext -WarningAction $warningAction
        if (-not $context) {
            Write-Host "❌ No Azure context found" -ForegroundColor Red
            Write-Host "   Please run Connect-AzAccount first to authenticate with Azure." -ForegroundColor Yellow
            return
        }
        # Check if resource group exists
        $resourceGroup = Get-AzResourceGroup -Name $computeResourceGroupName -ErrorAction SilentlyContinue -WarningAction $warningAction
        if (-not $resourceGroup) {
            Write-Host "✅ No Azure resources exist yet - nothing to migrate!" -ForegroundColor Green
            return
        }
        # Get migrations
        $migrationsNeeded = Get-FunctionsRequiringMigration -FunctionTemplatesPath $FunctionTemplatesPath -ComputeResourcesArmTemplatePath $ComputeResourcesArmTemplatePath -ResourceGroupName $computeResourceGroupName -SolutionAbbreviation $SolutionAbbreviation -EnvironmentAbbreviation $EnvironmentAbbreviation
        if ($migrationsNeeded.Count -eq 0) {
            Write-Host "✅ No functions require migration!" -ForegroundColor Green
            return
        }
    }
    # Check if we're in WhatIf mode or if connection strings are provided for actual execution
    if (-not $WhatIf -and ([string]::IsNullOrEmpty($SyncJobsDBConnectionString) -or [string]::IsNullOrEmpty($ADFDBConnectionString))) {
        Write-Host "⚠️  Migration is needed but connection strings are required for execution" -ForegroundColor Yellow
        Write-Host "   Please provide:" -ForegroundColor Gray
        if ([string]::IsNullOrEmpty($SyncJobsDBConnectionString)) {
            Write-Host "   • -SyncJobsDBConnectionString (sqlDatabaseConnectionString from KeyVault)" -ForegroundColor Gray
        }
        if ([string]::IsNullOrEmpty($ADFDBConnectionString)) {
            Write-Host "   • -ADFDBConnectionString (sqlServerBasicConnectionString from KeyVault)" -ForegroundColor Gray
        }
        Write-Host ""
        Write-Host "   Or run with -WhatIf to see what would be changed without requiring connection strings" -ForegroundColor Cyan
        return
    }
    Write-Host "🚀 Starting Flex Consumption Migration" -ForegroundColor Cyan
    Write-Host "   📁 Function Templates: $FunctionTemplatesPath" -ForegroundColor Gray
    Write-Host "   🏷️  Solution: $SolutionAbbreviation-$EnvironmentAbbreviation" -ForegroundColor Gray
    Write-Host "   🔄 Mode: $(if ($WhatIf) { 'Simulation (WhatIf)' } else { 'Execution' })" -ForegroundColor Gray
    Write-Host ""
    Write-Host "🎯 Target Resource Group: $computeResourceGroupName" -ForegroundColor Cyan

    # Set up SQL firewall if needed (not in WhatIf mode)
    $dataResourceGroupName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $sqlServerName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"

    if (-not $WhatIf -and -not $SkipFirewallCheck -and -not [string]::IsNullOrEmpty($SyncJobsDBConnectionString)) {
        Write-Host ""
        Write-Host "🔒 Checking SQL Server firewall access..." -ForegroundColor Cyan
        Set-UpdateSqlServerFirewallRule -ResourceGroupName $dataResourceGroupName -SqlServerName $sqlServerName
    }

    try {
        # Display migration summary
        Show-MigrationSummary -MigrationsNeeded $migrationsNeeded -SolutionAbbreviation $SolutionAbbreviation -EnvironmentAbbreviation $EnvironmentAbbreviation
        if ($WhatIf) {
            Write-Host ""
            Write-Host "🔍 SIMULATION MODE (WhatIf) - No changes will be made" -ForegroundColor Magenta
            Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Magenta
            # Show detailed actions
            Show-MigrationSummary -MigrationsNeeded $migrationsNeeded -SolutionAbbreviation $SolutionAbbreviation -EnvironmentAbbreviation $EnvironmentAbbreviation -Detailed
            Write-Host ""
            Write-Host "💡 To execute these changes, run the script without the -WhatIf parameter" -ForegroundColor Cyan
            Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Magenta
            return
        }
        else {
            if (-not $SkipConfirmation) {
                Write-Host ""
                Write-Host "⚠️  This will DELETE existing function apps and service plans!" -ForegroundColor Red
                Write-Host "   The deployment process will recreate them with the correct SKU." -ForegroundColor Yellow
                $confirmation = Read-Host "Do you want to continue? (yes/no)"
                if ($confirmation -notmatch '^(y|yes)$') {
                    Write-Host "❌ Migration cancelled by user" -ForegroundColor Red
                    return
                }
            }
            else {
                Write-Host ""
                Write-Host "🚀 Confirmation skipped - proceeding with migration automatically" -ForegroundColor Yellow
            }
        }
        # Execute migrations
        Write-Host ""
        Write-Host "🔄 Executing migrations..." -ForegroundColor Cyan
        foreach ($migration in $migrationsNeeded) {
            Write-Host ""
            Write-Host "  🔧 Processing: $($migration.FunctionType)" -ForegroundColor Cyan
            # Remove Azure resources
            Remove-FunctionAppAndServicePlan -FunctionName $migration.FunctionName `
                                            -ServicePlanName $migration.ServicePlanName `
                                            -ResourceGroupName $computeResourceGroupName `
                                            -WhatIf:$WhatIf
            # Remove SQL permissions
            Remove-SqlDatabasePermissions -IdentityName $migration.ManagedIdentityName `
                                         -SyncJobsDBConnectionString $SyncJobsDBConnectionString `
                                         -ADFDBConnectionString $ADFDBConnectionString `
                                         -SolutionAbbreviation $SolutionAbbreviation `
                                         -EnvironmentAbbreviation $EnvironmentAbbreviation `
                                         -WhatIf:$WhatIf
        }
        # Final summary
        $duration = (Get-Date) - $startTime
        Write-Host ""
        Write-Host "✅ Migration preparation completed!" -ForegroundColor Green
        Write-Host "   Duration: $($duration.ToString('mm\:ss'))" -ForegroundColor Gray
        Write-Host "   Functions processed: $($migrationsNeeded.Count)" -ForegroundColor Gray
        if (-not $WhatIf) {
            Write-Host ""
            Write-Host "🚀 Next Steps:" -ForegroundColor Cyan
            Write-Host "   1. Run your Deploy-Resources.ps1 script" -ForegroundColor White
            Write-Host "   2. The functions will be recreated with Flex Consumption SKU" -ForegroundColor White
            Write-Host "   3. SQL permissions will be automatically recreated" -ForegroundColor White
        }
    }
    catch {
        Write-Host ""
        Write-Host "❌ Migration failed: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "   Stack trace: $($_.Exception.StackTrace)" -ForegroundColor DarkRed
        # Return instead of throw to allow deployment to continue
        return
    }
}