function Set-Subscription {
    param (
        [Parameter(Mandatory = $true)]
        [string]$ScriptsDirectory
    )

    . ($ScriptsDirectory + '\Add-AzAccountIfNeeded.ps1')
    Add-AzAccountIfNeeded | Out-Null

    if (-not $SubscriptionId) {
        Write-Host "`nCurrent subscription:`n"
        $currentSubscription = (Get-AzContext).Subscription
        Write-Host "$($currentSubscription.Name) -  $($currentSubscription.Id)"
        Write-Host "`n"
        $SubscriptionId = Read-Host -Prompt "If you would like to use other subscription than '$($currentSubscription.Name)' `nprovide the subscription id, otherwise press enter to continue."
    }

    if ($SubscriptionId) {
        Set-AzContext -SubscriptionId $SubscriptionId
        $currentSubscription = (Get-AzContext).Subscription
        Write-Host "`nSelected subscription: $($currentSubscription.Name) -  $($currentSubscription.Id)"
    }

    return $SubscriptionId;
}

function Set-ResourceProviders {
    foreach ($namespace in @("Microsoft.ServiceBus", "Microsoft.Insights", "Microsoft.OperationalInsights", "Microsoft.AlertsManagement", "Microsoft.Storage", "Microsoft.AppConfiguration", "Microsoft.Sql", "Microsoft.Web", "Microsoft.DataFactory")) {
        Write-Host "Checking if the resource provider $namespace is registered..."
        $provider = Get-AzResourceProvider -ProviderNamespace $namespace

        if ($provider.Where({ $_.RegistrationState -ne "Registered" }).Count -gt 0) {
            Write-Host "$namespace is not registered. Registering..."
            Register-AzResourceProvider -ProviderNamespace $namespace
        }

        Write-Host "$namespace is registered."
    }
}

function Set-GMMResources {
    param (
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$Location,
        [Parameter(Mandatory = $true)]
        [string]$TemplateFilePath,
        [Parameter(Mandatory = $true)]
        [string]$ParameterFilePath
    )

    # deploy resources
    Write-Host "`nDeploying resources"
    New-AzDeployment `
        -TemplateFile $TemplateFilePath `
        -TemplateParameterFile $ParameterFilePath `
        -solutionAbbreviation $SolutionAbbreviation `
        -environmentAbbreviation $EnvironmentAbbreviation `
        -location $Location
}

function Set-SqlServerFirewallRule {
    param (
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$Location,
        [Parameter(Mandatory = $true)]
        [string]$ipAddress
    )

    Write-Host "`nSetting SQL Server firewall rule"
    $sqlIPRule = Get-AzSqlServerFirewallRule `
        -FirewallRuleName "InitialDeployment" `
        -ResourceGroupName $dataResourceGroup `
        -ServerName "$SolutionAbbreviation-data-$EnvironmentAbbreviation" `
        -ErrorAction SilentlyContinue

    if ($null -eq $sqlIPRule) {
        Write-Host "Adding firewall rule for SQL Server"
        New-AzSqlServerFirewallRule `
            -ResourceGroupName $dataResourceGroup `
            -ServerName "$SolutionAbbreviation-data-$EnvironmentAbbreviation" `
            -FirewallRuleName "InitialDeployment" `
            -StartIpAddress $ipAddress `
            -EndIpAddress $ipAddress
    }
}

function Set-SQLServerPermissions {
    param (
        [Parameter(Mandatory = $true)]
        [string]$ConnectionString,
        [Parameter(Mandatory = $true)]
        [string]$ComputeResourceGroup
    )

    # SQL Permissions
    Write-Host "`nGranting permissions to SQL database"

    $context = [Microsoft.Azure.Commands.Common.Authentication.Abstractions.AzureRmProfileProvider]::Instance.Profile.DefaultContext
    $sqlToken = [Microsoft.Azure.Commands.Common.Authentication.AzureSession]::Instance.AuthenticationFactory.Authenticate($context.Account, $context.Environment, $context.Tenant.Id.ToString(), $null, [Microsoft.Azure.Commands.Common.Authentication.ShowDialog]::Never, $null, "https://database.windows.net").AccessToken
    $connection = New-Object System.Data.SqlClient.SqlConnection
    $connection.ConnectionString = $ConnectionString
    $connection.AccessToken = $sqlToken

    $sqlScript = "IF NOT EXISTS (SELECT * FROM sys.database_principals WHERE name = N'$($context.Account.Id)')
        BEGIN
            CREATE USER [$($context.Account.Id)] FROM EXTERNAL PROVIDER
            ALTER ROLE db_datareader ADD MEMBER [$($context.Account.Id)]
            ALTER ROLE db_datawriter ADD MEMBER [$($context.Account.Id)]
            ALTER ROLE db_ddladmin ADD MEMBER [$($context.Account.Id)]
        END"

    Write-Host "Granting permissions to SQL database for $($context.Account.Id)"
    $roleCommand = $connection.CreateCommand()
    $roleCommand.CommandText = $sqlScript
    $connection.Open()
    [void]$roleCommand.ExecuteNonQuery()
    $connection.Close()
    $roleCommand.Dispose()

    $functionApps = Get-AzResource -ResourceGroupName $ComputeResourceGroup -ResourceType "Microsoft.Web/sites"
    $connection.Open()

    foreach ($functionApp in $functionApps) {

        $functionSqlScript = "IF NOT EXISTS (SELECT * FROM sys.database_principals WHERE name = N'$($functionApp.Name)')
        BEGIN
            CREATE USER [$($functionApp.Name)] FROM EXTERNAL PROVIDER
            ALTER ROLE db_datareader ADD MEMBER [$($functionApp.Name)]
            ALTER ROLE db_datawriter ADD MEMBER [$($functionApp.Name)]
        END"

        Write-Host "Granting permissions to SQL database for $($functionApp.Name)"

        $roleCommand = $connection.CreateCommand()
        $roleCommand.CommandText = $functionSqlScript
        [void]$roleCommand.ExecuteNonQuery()
        $roleCommand.Dispose()
    }

    $connection.Close()
}

function Set-RBACPermissions {
    param (
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$ScriptsDirectory
    )

    # grant permissions to resources
    Write-Host "`nGranting permissions to resources"

    . ($ScriptsDirectory + '\Set-PostDeploymentRoles.ps1')
    Set-PostDeploymentRoles -SolutionAbbreviation $SolutionAbbreviation -EnvironmentAbbreviation $EnvironmentAbbreviation
}

function Set-DBMigrations {
    param (
        [Parameter(Mandatory = $true)]
        [string]$ConnectionString,
        [Parameter(Mandatory = $true)]
        [string]$ScriptsDirectory
    )

    # Run Migrations
    # Create bundle
    # PS - https://learn.microsoft.com/en-us/ef/core/cli/powershell#common-parameters
    # dotnet - https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying?tabs=dotnet-core-cli
    # dotnet ef migrations bundle --context GMMContext

    Write-Host "`nApplying database migrations"
    Write-Host "Interactively sign in to Azure AD to apply database migrations"

    . ("$ScriptsDirectory\efbundle.exe") --connection "$ConnectionString;Authentication=Active Directory Interactive;"
}

function Set-FunctionAppCode {
    param (
        [Parameter(Mandatory = $true)]
        [string]$ComputeResourceGroup,
        [Parameter(Mandatory = $true)]
        [string]$FunctionsPackagesDirectory,
        [Parameter(Mandatory = $true)]
        [string]$WebApiPackagesDirectory
    )

    # publish function apps code
    Write-Host "`nPublishing function apps code"

    $functionApps = Get-AzFunctionApp -ResourceGroupName $ComputeResourceGroup
    foreach ($functionApp in $functionApps) {

        Write-Host "Publishing code for function app $($functionApp.Name)"

        $functionName = $functionApp.Name.Split("-")[3]
        Publish-AzWebApp `
            -ResourceGroupName $computeResourceGroup `
            -Name $functionApp.Name `
            -ArchivePath "$FunctionsPackagesDirectory\$functionName.zip" `
            -Force `
    }

    # publish web api code
    Write-Host "`nPublishing code for webapi app $ComputeResourceGroup-webapi"
    $webApi = Get-AzWebApp -ResourceGroupName $ComputeResourceGroup -Name "$ComputeResourceGroup-webapi"
    $webApiName = $webApi.Name.Split("-")[3]
    Publish-AzWebApp `
        -ResourceGroupName $computeResourceGroup `
        -Name $webApi.Name `
        -ArchivePath "$WebApiPackagesDirectory\$webApiName.zip" `
        -Force `

}

function Disable-KeyVaultFirewallRules {
    param (
        [Parameter(Mandatory = $true)]
        [string[]]$ResourceGroups
    )

    # disable firewall rules for key vaults
    Write-Host "Disabling firewall rules for key vaults"

    # apply firewall rules to key vaults
    foreach ($resourceGroup in $ResourceGroups) {
        $keyVaults = Get-AzKeyVault -ResourceGroupName $resourceGroup
        foreach ($keyVault in $keyVaults) {
            $keyVaultName = $keyVault.VaultName
            $keyVaultResourceGroup = $keyVault.ResourceGroupName

            Write-Host "Disabling firewall rules for key vault $keyVaultName in resource group $keyVaultResourceGroup"

            Update-AzKeyVaultNetworkRuleSet `
                -VaultName $keyVaultName `
                -ResourceGroupName $keyVaultResourceGroup `
                -Bypass AzureServices `
                -DefaultAction Allow
        }
    }
}

function Set-KeyVaultFirewallRules {
    param (
        [Parameter(Mandatory = $true)]
        [string[]]$ResourceGroups,
        [Parameter(Mandatory = $true)]
        [string]$ipAddress,
        [Parameter(Mandatory = $true)]
        [string]$ScriptsDirectory
    )

    # enable firewall rules for key vaults
    Write-Host "Enabling firewall rules for key vaults"

    # get ip addresses for firewall rules
    . ($ScriptsDirectory + '\Get-FirewallIPRules.ps1') -FolderPathToSaveIpRules $ScriptsDirectory
    $ipRules = Get-Content "$ScriptsDirectory\ipRules.txt"
    $ipRules += $ipAddress

    # apply firewall rules to key vaults
    foreach ($resourceGroup in $ResourceGroups) {
        $keyVaults = Get-AzKeyVault -ResourceGroupName $resourceGroup
        foreach ($keyVault in $keyVaults) {
            $keyVaultName = $keyVault.VaultName
            $keyVaultResourceGroup = $keyVault.ResourceGroupName

            Write-Host "Enabling firewall rules for key vault $keyVaultName in resource group $keyVaultResourceGroup"

            Update-AzKeyVaultNetworkRuleSet `
                -VaultName $keyVaultName `
                -ResourceGroupName $keyVaultResourceGroup `
                -Bypass AzureServices `
                -DefaultAction Deny `
                -IpAddressRange $ipRules
        }
    }
}

function Deploy-Resources {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$Location,
        [Parameter(Mandatory = $true)]
        [string]$TemplateFilePath,
        [Parameter(Mandatory = $true)]
        [string]$ParameterFilePath,
        [Parameter(Mandatory = $false)]
        [bool]$SkipResourceProvidersCheck = $false
    )

    # define the resource groups
    $prereqsResourceGroup = "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation"
    $dataResourceGroup = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $computeResourceGroup = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation"
    $resourceGroups = @($prereqsResourceGroup, $dataResourceGroup, $computeResourceGroup)
    $ipAddress = (Invoke-WebRequest -uri “https://api.ipify.org/”).Content

    $scriptsDirectory = Split-Path $PSScriptRoot -Parent

    Set-Subscription -ScriptsDirectory "$scriptsDirectory\Scripts"
    if (!$SkipResourceProvidersCheck) {
        Set-ResourceProviders
    }

    Disable-KeyVaultFirewallRules -ResourceGroups $resourceGroups

    Set-GMMResources `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -Location $Location `
        -TemplateFilePath $TemplateFilePath `
        -ParameterFilePath $ParameterFilePath

    Start-Sleep -Seconds 30

    Disable-KeyVaultFirewallRules -ResourceGroups $resourceGroups

    Set-SqlServerFirewallRule `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -Location $Location `
        -ipAddress $ipAddress

    # retrieve SQL connection strings
    # Basic connection string
    $connectionString = Get-AzKeyVaultSecret `
        -VaultName "$SolutionAbbreviation-data-$EnvironmentAbbreviation" `
        -Name "sqlDatabaseConnectionString" `
        -AsPlainText

    Set-SQLServerPermissions `
    -ConnectionString $connectionString `
    -ComputeResourceGroup $computeResourceGroup

    Set-RBACPermissions `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -ScriptsDirectory "$scriptsDirectory\Scripts\PostDeployment"

    Set-DBMigrations `
    -ConnectionString $connectionString `
    -ScriptsDirectory $scriptsDirectory

    Set-FunctionAppCode `
        -ComputeResourceGroup $computeResourceGroup `
        -FunctionsPackagesDirectory "$scriptsDirectory\function_packages" `
        -WebApiPackagesDirectory "$scriptsDirectory\webapi_package"

    Set-KeyVaultFirewallRules `
        -ResourceGroups $resourceGroups `
        -ipAddress $ipAddress `
        -ScriptsDirectory "$scriptsDirectory\Scripts"
}


