$ErrorActionPreference = "Stop"
<#
.SYNOPSIS
This script will deploy all resources and grant permissions

.PARAMETER SolutionAbbreviation
Abbreviation used to denote the overall solution (or application)

.PARAMETER EnvironmentAbbreviation
Abbreviation for the environment

.PARAMETER Location
Location where the resources will be deployed

.PARAMETER TemplateFilesDirectory
Template files directory
Absolute path.

.PARAMETER ParameterFilePath
Parameter file path.
Absolute path.

.PARAMETER SubscriptionId
Optional.
Subscription Id where the resources will be deployed.

.PARAMETER SkipResourceProvidersCheck
Optional.
Flag to skip the resource providers check.

.PARAMETER SetUserAssignedManagedIdentityPermissions
Optional.
If you are using a user-assigned managed identity, set this flag to true to assign the necessary permissions to the managed identity.

.EXAMPLE
Deploy-Resources    -SolutionAbbreviation "<solution-abbreviation>" `
                    -EnvironmentAbbreviation "<environment-abbreviation>" `
                    -Location "<location>" `
                    -TemplateFilesDirectory "<template-file-path>" `
                    -ParameterFilePath "<parameter-file-path>" `
                    -SubscriptionId "<subscription-id>" `
                    -Verbose
#>

$maxRetries = 3

function Retry-Operation {
    param(
        [Parameter(Mandatory = $true)]
        [ScriptBlock]$Operation,
        [Parameter(ValueFromRemainingArguments = $true)]
        $params,
        [Parameter(Mandatory = $true)]
        [string]$OperationName
    )

    # Initialize the retry counter
    $retryCount = 0

    do {
        try {
            & $Operation @params
            break
        }
        catch {

            Write-Warning $_.Exception.Message

            $retryCount++
            if ($retryCount -ge $maxRetries) {
                throw
            }

            Write-Warning "'$OperationName' failed, retrying... ($retryCount/$maxRetries)"
            Start-Sleep -Seconds (5 * $retryCount)
        }
    } while ($true)
}

function Deploy-PostDeploymentUpdates {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$ScriptsDirectory
    )

    . ($ScriptsDirectory + '\main.ps1')
    $currentContext = Get-AzContext
    Update-GmmMigrationIfNeeded `
        -SubscriptionName $currentContext.Subscription.Name `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation
}

function Set-Subscription {
    param (
        [Parameter(Mandatory = $false)]
        [string]$SubscriptionId,
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

function Get-TemplateAsHashtable {
    param (
        [Parameter(Mandatory = $true)]
        [string]$TemplateFilePath
    )

    $TemplateFileText = [System.IO.File]::ReadAllText($TemplateFilePath)
    $TemplateObject = ConvertFrom-Json $TemplateFileText -AsHashtable
    return $TemplateObject
}

function Get-TemplateParameters {
    param (
        [Parameter(Mandatory = $true)]
        [string]$TemplateFilePath,
        [Parameter(Mandatory = $true)]
        [string]$ParametersFilePath,
        [Parameter(Mandatory = $false)]
        [Hashtable]$AdditionalParameters = @{ parameters = @{} }
    )

    $TemplateObject = Get-TemplateAsHashtable -TemplateFilePath $TemplateFilePath
    $ParametersObject = Get-TemplateAsHashtable -TemplateFilePath $ParametersFilePath

    $commonParametersObject = @{}

    # add those with a default value
    $TemplateObject.parameters.Keys | ForEach-Object {
        $parameter = $TemplateObject.parameters[$_]
        if ($parameter.Keys -contains "defaultValue") {
            $commonParametersObject[$_] = $parameter.defaultValue
        }
    }

    # add from the additional parameters
    if ($AdditionalParameters.parameters.Keys.Count -gt 0) {
        $TemplateObject.parameters.Keys | ForEach-Object {
            if ($AdditionalParameters.parameters.Keys -contains $_) {
                $commonParametersObject[$_] = $AdditionalParameters.parameters[$_].value
            }
        }
    }

    # add (or overwrite) from the parameters file
    $TemplateObject.parameters.Keys | ForEach-Object {
        if ($ParametersObject.parameters.Keys -contains $_) {
            $commonParametersObject[$_] = $ParametersObject.parameters[$_].value
        }
    }

    return $commonParametersObject
}

function Check-IfKeyVaultSecretExists {
    param (
        [Parameter(Mandatory = $true)]
        [string]$VaultName,
        [Parameter(Mandatory = $true)]
        [string]$SecretName
    )

    $secret = Get-AzKeyVaultSecret -VaultName $VaultName -Name $SecretName -ErrorAction SilentlyContinue
    return $null -ne $secret
}

function Set-KeyVaultRole {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $True)]
        [string] $ObjectId,
        [Parameter(Mandatory = $True)]
        [string] $Scope,
        [Parameter(Mandatory = $True)]
        [string] $RoleDefinitionName,
        [Parameter(Mandatory = $True)]
        [string] $KeyVaultName
    )

    if ($null -eq (Get-AzRoleAssignment -ObjectId $ObjectId -Scope $Scope -RoleDefinitionName $RoleDefinitionName)) {
        New-AzRoleAssignment -ObjectId $ObjectId -Scope $Scope -RoleDefinitionName $RoleDefinitionName;
        Write-Host "Added role $RoleDefinitionName to $ObjectId on the $KeyVaultName keyvault.";
    }
    else {
        Write-Host "$ObjectId already has  $RoleDefinitionName role on $KeyVaultName.";
    }
}

function Set-AdminKeyVaultRoles {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $True)]
        [string] $UserObjectId,
        [Parameter(Mandatory = $True)]
        [string] $KeyVaultName,
        [Parameter(Mandatory = $True)]
        [string] $ResourceGroupName
    )
    $keyVault = `
        Get-AzKeyVault `
        -ResourceGroupName $ResourceGroupName `
        -Name $KeyVaultName

    Set-KeyVaultRole `
        -ObjectId $UserObjectId `
        -Scope $keyVault.ResourceId `
        -RoleDefinitionName "Key Vault Data Access Administrator" `
        -KeyVaultName $keyVault.VaultName

    Set-KeyVaultRole `
        -ObjectId $UserObjectId `
        -Scope $keyVault.ResourceId `
        -RoleDefinitionName "Key Vault Administrator" `
        -KeyVaultName $keyVault.VaultName
}

function Set-PrereqResources {
    param (
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$TemplateFilePath,
        [Parameter(Mandatory = $true)]
        [string]$ParameterFilePath,
        [Parameter(Mandatory = $false)]
        [Hashtable]$AdditionalParameters = @{ parameters = @{} },
        [Parameter(Mandatory = $false)]
        [bool] $SetRBACPermissions
    )

    $directoryPath = $TemplateFilePath
    $currentUser = Get-AzADUser -SignedIn
    $prereqsResourceGroup = "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation"

    # deploy prereq resources
    Write-Host "`nCreating prereqs resources"
    $prereqResourcesParameters = `
        Get-TemplateParameters `
        -TemplateFilePath "$directoryPath\prereqResources.json" `
        -ParametersFilePath $ParameterFilePath `
        -AdditionalParameters $AdditionalParameters

    New-AzResourceGroupDeployment `
        -ResourceGroupName $prereqsResourceGroup `
        -TemplateFile "$directoryPath\prereqResources.json" `
        -TemplateParameterObject $prereqResourcesParameters

    # grant permissions to prereqs key vault
    if ($setRBACPermissions -eq $true) {
        Set-AdminKeyVaultRoles `
            -UserObjectId $currentUser.Id `
            -KeyVaultName "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation" `
            -ResourceGroupName $prereqsResourceGroup
    }
}

function Set-DataResources {
    param (
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$TemplateFilePath,
        [Parameter(Mandatory = $true)]
        [string]$ParameterFilePath,
        [Parameter(Mandatory = $false)]
        [Hashtable]$AdditionalParameters = @{ parameters = @{} },
        [Parameter(Mandatory = $false)]
        [bool] $SetRBACPermissions
    )

    Write-Host "`nCreating data resources"

    $directoryPath = $TemplateFilePath
    $currentUser = Get-AzADUser -SignedIn
    $dataResourceGroup = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"

    $dataResourcesParameters = `
        Get-TemplateParameters `
        -TemplateFilePath "$directoryPath\dataResources.json" `
        -ParametersFilePath $ParameterFilePath `
        -AdditionalParameters $AdditionalParameters

    New-AzResourceGroupDeployment `
        -ResourceGroupName $dataResourceGroup `
        -TemplateFile "$directoryPath\dataResources.json" `
        -TemplateParameterObject $dataResourcesParameters

    # grant permissions to data key vault
    if ($setRBACPermissions -eq $true) {
        Set-AdminKeyVaultRoles `
            -UserObjectId $currentUser.Id `
            -KeyVaultName "$SolutionAbbreviation-data-$EnvironmentAbbreviation" `
            -ResourceGroupName $dataResourceGroup
    }
}

function Set-ComputeResources {
    param (
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$TemplateFilePath,
        [Parameter(Mandatory = $true)]
        [string]$ParameterFilePath,
        [Parameter(Mandatory = $false)]
        [Hashtable]$AdditionalParameters = @{ parameters = @{} }
    )

    $directoryPath = $TemplateFilePath
    $computeResourceGroup = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation"

    Write-Host "`nCreating compute resources"
    $computeResourcesParameters = `
        Get-TemplateParameters `
        -TemplateFilePath "$directoryPath\computeResources.json" `
        -ParametersFilePath $ParameterFilePath `
        -AdditionalParameters $AdditionalParameters

    New-AzResourceGroupDeployment `
        -ResourceGroupName $computeResourceGroup `
        -TemplateFile "$directoryPath\computeResources.json" `
        -TemplateParameterObject $computeResourcesParameters

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
    $directoryPath = $TemplateFilePath

    $prereqsResourceGroup = "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation"
    $dataResourceGroup = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $computeResourceGroup = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation"

    $commonParametersObject = @{ parameters = @{} }
    $commonParametersObject.parameters["solutionAbbreviation"] = @{"value" = $SolutionAbbreviation }
    $commonParametersObject.parameters["environmentAbbreviation"] = @{"value" = $EnvironmentAbbreviation }
    $commonParametersObject.parameters["prereqsResourceGroupName"] = @{"value" = $prereqsResourceGroup }
    $commonParametersObject.parameters["dataResourceGroupName"] = @{"value" = $dataResourceGroup }
    $commonParametersObject.parameters["computeResourceGroupName"] = @{"value" = $computeResourceGroup }
    $commonParametersObject.parameters["prereqsKeyVaultName"] = @{"value" = $prereqsResourceGroup }
    $commonParametersObject.parameters["dataKeyVaultName"] = @{"value" = $dataResourceGroup }
    $commonParametersObject.parameters["computeKeyVaultName"] = @{"value" = $computeResourceGroup }
    $commonParametersObject.parameters["appConfigurationName"] = @{"value" = "$SolutionAbbreviation-appConfig-$EnvironmentAbbreviation" }
    $commonParametersObject.parameters["apiServiceBaseUri"] = @{"value" = "https://$SolutionAbbreviation-compute-$EnvironmentAbbreviation-webapi.azurewebsites.net" }

    $parameterObject = Get-TemplateAsHashtable -TemplateFilePath $ParameterFilePath
    $setRBACPermissions = $parameterObject.parameters["setRBACPermissions"].value ?? $false;
    $certificateName = $parameterObject.parameters["certificateName"].value ?? "not-set";

    # deploy resource groups
    Write-Host "`nCreating resource groups"
    $resourceGroupsParameters = `
        Get-TemplateParameters `
        -TemplateFilePath "$directoryPath\resourceGroups.json" `
        -ParametersFilePath $ParameterFilePath `
        -AdditionalParameters $commonParametersObject

    New-AzDeployment `
        -TemplateFile "$directoryPath\resourceGroups.json" `
        -TemplateParameterObject $resourceGroupsParameters `
        -Location $Location

    # deploy prereq resources
    Retry-Operation `
        -Operation ${function:Set-PrereqResources} `
        -OperationName "Create prereq resources" `
        -params @{
        SolutionAbbreviation    = $SolutionAbbreviation
        EnvironmentAbbreviation = $EnvironmentAbbreviation
        TemplateFilePath        = $TemplateFilePath
        ParameterFilePath       = $ParameterFilePath
        AdditionalParameters    = $commonParametersObject
        SetRBACPermissions      = $setRBACPermissions
    }

    # creating app registrations
    Write-Host "`nCreating app registrations"
    $appRegistrations = `
        Set-GMMAppRegistrations `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -ScriptsDirectory "$scriptsDirectory\Scripts" `
        -SecondaryTenantId $SecondaryTenantId `
        -CertificateName $certificateName

    # add app registrations to common parameters
    $commonParametersObject.parameters["apiAppClientId"] = @{ "value" = $appRegistrations.APIApplicationId }
    $commonParametersObject.parameters["uiAppTenantId"] = @{ "value" = $appRegistrations.UITenantId }
    $commonParametersObject.parameters["uiAppClientId"] = @{ "value" = $appRegistrations.UIApplicationId }

    # deploy data resources
    Retry-Operation `
        -Operation ${function:Set-DataResources} `
        -OperationName "Create data resources" `
        -params @{
        SolutionAbbreviation    = $SolutionAbbreviation
        EnvironmentAbbreviation = $EnvironmentAbbreviation
        TemplateFilePath        = $TemplateFilePath
        ParameterFilePath       = $ParameterFilePath
        AdditionalParameters    = $commonParametersObject
        SetRBACPermissions      = $setRBACPermissions
    }

    # deploy compute resources
    Retry-Operation `
        -Operation ${function:Set-ComputeResources} `
        -OperationName "Create compute resources" `
        -params @{
        SolutionAbbreviation    = $SolutionAbbreviation
        EnvironmentAbbreviation = $EnvironmentAbbreviation
        TemplateFilePath        = $TemplateFilePath
        ParameterFilePath       = $ParameterFilePath
        AdditionalParameters    = $commonParametersObject
    }

    # deploy ADF resources
    Write-Host "`nCreating ADF resources"
    $adfResourcesParameters = `
        Get-TemplateParameters `
        -TemplateFilePath "$directoryPath\adfHRResources.json" `
        -ParametersFilePath $ParameterFilePath `
        -AdditionalParameters $commonParametersObject

    $adfDataSecrets = @("sqlAdminPassword", "azureUserReaderUrl", "azureUserReaderKey", "adfStorageAccountConnectionString")
    foreach ($secret in $adfDataSecrets) {
        $secretExists = Check-IfKeyVaultSecretExists -VaultName $dataResourceGroup -SecretName $secret
        if (-not $secretExists) {
            $secretValue = New-Object System.Security.SecureString
            "not-set".ToCharArray() | ForEach-Object { $secretValue.AppendChar($_) }
            Set-AzKeyVaultSecret -VaultName $dataResourceGroup -Name $secret -SecretValue $secretValue
        }
    }

    New-AzResourceGroupDeployment `
        -ResourceGroupName $dataResourceGroup `
        -TemplateFile "$directoryPath\adfHRResources.json" `
        -TemplateParameterObject $adfResourcesParameters

    Write-Host "`nResources deployed"

    return @{
        AppRegistrations = $appRegistrations
    }
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
        [string]$ConnectionStringADF,
        [Parameter(Mandatory = $true)]
        [string]$ComputeResourceGroup,
        [Parameter(Mandatory = $true)]
        [string]$DataResourceGroup
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

    # ADF Permissions
    $dataFactoryName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation-adf"
    $dataFactory = Get-AzDataFactoryV2 -ResourceGroupName $DataResourceGroup -Name $dataFactoryName -ErrorAction SilentlyContinue
    $functionAppsADF = $functionApps | Where-Object { $_.Name -match "-webapi" -or $_.Name -match "-SqlMembershipObtainer"}

    if($null -ne $dataFactory) {

        $connectionADF = New-Object System.Data.SqlClient.SqlConnection
        $connectionADF.ConnectionString = $ConnectionStringADF
        $connectionADF.AccessToken = $sqlToken
        $connectionADF.Open()

        $dataFactorySqlScript = "IF NOT EXISTS (SELECT * FROM sys.database_principals WHERE name = N'$dataFactoryName')
        BEGIN
            CREATE USER [$dataFactoryName] FROM EXTERNAL PROVIDER
            ALTER ROLE db_datareader ADD MEMBER [$dataFactoryName]
            ALTER ROLE db_datawriter ADD MEMBER [$dataFactoryName]
            ALTER ROLE db_ddladmin ADD MEMBER [$dataFactoryName]
        END"

        Write-Host "Granting permissions to SQL database for $dataFactoryName"

        $roleCommandADF = $connectionADF.CreateCommand()
        $roleCommandADF.CommandText = $dataFactorySqlScript
        [void]$roleCommandADF.ExecuteNonQuery()
        $roleCommandADF.Dispose()

        foreach ($functionApp in $functionAppsADF) {

            $functionSqlScript = "IF NOT EXISTS (SELECT * FROM sys.database_principals WHERE name = N'$($functionApp.Name)')
            BEGIN
                CREATE USER [$($functionApp.Name)] FROM EXTERNAL PROVIDER
                ALTER ROLE db_datareader ADD MEMBER [$($functionApp.Name)]
                ALTER ROLE db_datawriter ADD MEMBER [$($functionApp.Name)]
            END"

            Write-Host "Granting permissions to SQL database for $($functionApp.Name)"

            $roleCommandADF = $connectionADF.CreateCommand()
            $roleCommandADF.CommandText = $functionSqlScript
            [void]$roleCommandADF.ExecuteNonQuery()
            $roleCommandADF.Dispose()
        }

        $connectionADF.Close()
    }
}

function Set-RBACPermissions {
    param (
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$ScriptsDirectory,
        [Parameter(Mandatory = $false)]
        [bool] $SetUserAssignedManagedIdentityPermissions = $false
    )

    # grant permissions to resources
    Write-Host "`nGranting permissions to resources"

    . ($ScriptsDirectory + '\Set-PostDeploymentRoles.ps1')
    Set-PostDeploymentRoles `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -SetUserAssignedManagedIdentityPermissions $SetUserAssignedManagedIdentityPermissions

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
        $packageFile = "$FunctionsPackagesDirectory\$functionName.zip"

        if (-not (Test-Path $packageFile)) {
            Write-Host "Package file not found: $packageFile"
            continue
        }

        Publish-AzWebApp `
            -ResourceGroupName $computeResourceGroup `
            -Name $functionApp.Name `
            -ArchivePath $packageFile `
            -Force
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
        $rgObject = Get-AzResourceGroup -Name $resourceGroup -ErrorAction SilentlyContinue
        if ($null -eq $rgObject) {
            continue
        }

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
        [string]$ScriptsDirectory,
        [Parameter(Mandatory = $true)]
        [string]$Region
    )

    # enable firewall rules for key vaults
    Write-Host "Enabling firewall rules for key vaults"

    # get ip addresses for firewall rules
    . ($ScriptsDirectory + '\Get-FirewallIPRules.ps1') -FolderPathToSaveIpRules $ScriptsDirectory -Regions $Region
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

function Stop-FunctionApps {
    param (
        [Parameter(Mandatory = $true)]
        [string]$ResourceGroupName
    )

    $rgObject = Get-AzResourceGroup -Name $ResourceGroupName -ErrorAction SilentlyContinue
    if ($null -eq $rgObject) {
        return
    }

    # stop function apps
    Write-Host "`nStopping function apps"

    $functionApps = Get-AzFunctionApp -ResourceGroupName $ResourceGroupName
    foreach ($functionApp in $functionApps) {
        Write-Host "Stopping function app $($functionApp.Name)"
        Stop-AzFunctionApp -ResourceGroupName $ResourceGroupName -Name $functionApp.Name -Force
    }
}

function Start-FunctionApps {
    param (
        [Parameter(Mandatory = $true)]
        [string]$ResourceGroupName
    )

    $rgObject = Get-AzResourceGroup -Name $ResourceGroupName -ErrorAction SilentlyContinue
    if ($null -eq $rgObject) {
        return
    }

    # start function apps
    Write-Host "`nStarting function apps"

    $functionApps = Get-AzFunctionApp -ResourceGroupName $ResourceGroupName
    foreach ($functionApp in $functionApps) {
        Write-Host "Starting function app $($functionApp.Name)"
        Start-AzFunctionApp -ResourceGroupName $ResourceGroupName -Name $functionApp.Name
    }
}

function Set-GMMAppRegistrations {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$ScriptsDirectory,
        [Parameter(Mandatory = $false)]
        [System.Nullable[Guid]]$SecondaryTenantId,
        [Parameter(Mandatory = $False)]
        [boolean] $SkipIfApplicationExists = $True,
        [Parameter(Mandatory = $False)]
        [string] $CertificateName
    )

    Write-Host "`nSetting GMM App Registrations"
    . ($ScriptsDirectory + '\Set-UIAzureADApplication.ps1')

    $currentContext = Get-AzContext
    $subscriptionName = $currentContext.Subscription.Name
    $mainTenantId = $currentContext.Tenant.Id

    $uiInformation = Set-UIAzureADApplication `
        -SubscriptionName $subscriptionName `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -TenantId $mainTenantId `
        -DevTenantId $SecondaryTenantId `
        -SaveToKeyVault $true `
        -SkipPrompts $true `
        -SkipIfApplicationExists $true `
        -Clean $false

    . ($ScriptsDirectory + '\Set-WebApiAzureADApplication.ps1')
    $apiInformation = Set-WebApiAzureADApplication `
        -SubscriptionName $subscriptionName `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -TenantId $mainTenantId `
        -DevTenantId $SecondaryTenantId `
        -SaveToKeyVault $true `
        -SkipPrompts $true `
        -SkipIfApplicationExists $true `
        -Clean $false

    . ($ScriptsDirectory + '\Set-GraphCredentialsAzureADApplication.ps1')
    $graphInformation = Set-GraphCredentialsAzureADApplication `
        -SubscriptionName $subscriptionName `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -TenantIdToCreateAppIn ($SecondaryTenantId ?? $mainTenantId) `
        -TenantIdWithKeyVault $mainTenantId `
        -SaveToKeyVault $true `
        -SkipPrompts $true `
        -SkipIfApplicationExists $true `
        -CertificateName $CertificateName `
        -Clean $false

    $null = Set-AzContext -Tenant $mainTenantId

    . ($ScriptsDirectory + '\Set-GMMSqlMembershipAzureADApplication.ps1')
    $sqlMembershipApp = Set-GMMSqlMembershipAzureADApplication `
        -SubscriptionName $subscriptionName `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -Clean $false `
        -SkipIfApplicationExists $true

    return @{
        UIApplicationId            = $uiInformation.ApplicationId;
        UITenantId                 = $uiInformation.TenantId;
        APIApplicationId           = $apiInformation.ApplicationId;
        APITenantId                = $apiInformation.TenantId;
        GraphApplicationId         = $graphInformation.ApplicationId;
        GraphTenantId              = $graphInformation.TenantId;
        SqlMembershipApplicationId = $sqlMembershipApp.ApplicationId;
        SqlMembershipTenantId      = $sqlMembershipApp.TenantId;
    }
}

function Set-ConfigureWebApps {
    param (
        [Parameter(Mandatory = $true)]
        [string]$WebApiName,
        [Parameter(Mandatory = $true)]
        [string]$UIWebAppName,
        [Parameter(Mandatory = $true)]
        [string]$UIAppRegistrationId,
        [Parameter(Mandatory = $true)]
        [string]$ComputeResourceGroup
    )

    # Set CORS for web apps
    $allowedOrigins = @()

    try {
        $customDomain = Get-AzStaticWebAppCustomDomain -Name $UIWebAppName -ResourceGroupName $ComputeResourceGroup
        if (-not [string]::IsNullOrEmpty($customDomain)) {
            $allowedOrigins += $customDomain.HostName
        }
    }
    catch {
        Write-Output "No custom domain associated with this web app."
    }

    $staticWebApp = Get-AzStaticWebApp -Name $UIWebAppName -ResourceGroupName $ComputeResourceGroup
    $allowedOrigins += "https://$($staticWebApp.DefaultHostname)"

    $webApi = Get-AzWebApp -ResourceGroupName $ComputeResourceGroup -Name $WebApiName
    $currentCORs = $webApi.SiteConfig.Cors.AllowedOrigins
    $newCORs = @()

    if ($null -eq $currentCORs) {
        $currentCORs = New-Object System.Collections.Generic.List[string]
    }

    foreach ($origin in $allowedOrigins) {
        if (-not $currentCORs.Contains($origin)) {
            $newCORs += $origin
        }
    }

    if ($newCORs.Count -gt 0) {

        # preserve the existing CORS settings
        $currentCORs | ForEach-Object {
            $newCORs += $_
        }

        $apiResourceParams = @{
            ResourceName      = $WebApiName
            ResourceType      = "Microsoft.Web/sites"
            ResourceGroupName = $ComputeResourceGroup
        }

        $webApiResource = Get-AzResource @apiResourceParams
        $webApiResource.Properties.siteConfig.cors = @{
            allowedOrigins = $newCORs
        }

        $webApiResource | Set-AzResource -Force
    }

    # Set UI Redirect URIs
    $uiApp = Get-AzADApplication -ApplicationId $UIAppRegistrationId
    $currentRedirectUris = $uiApp.Spa.RedirectUri
    $newRedirectUris = @()

    foreach ($origin in $allowedOrigins) {
        if (-not $currentRedirectUris.Contains($origin)) {
            $newRedirectUris += $origin
        }
    }

    if ($newRedirectUris.Count -gt 0) {

        # preserve the existing redirect URIs
        $currentRedirectUris | ForEach-Object {
            $newRedirectUris += $_
        }

        Update-AzADApplication `
            -ObjectId $uiApp.Id `
            -SPARedirectUri $newRedirectUris
    }
}

function Set-PublishUICode {
    param (
        [Parameter(Mandatory = $true)]
        [string]$UIClientId,
        [Parameter(Mandatory = $true)]
        [string]$UITenantId,
        [Parameter(Mandatory = $true)]
        [string]$WebApiClientId,
        [Parameter(Mandatory = $true)]
        [string]$WebApiBaseUri,
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$DataResourceGroup,
        [Parameter(Mandatory = $true)]
        [string]$ComputeResourceGroup,
        [Parameter(Mandatory = $true)]
        [string]$WebAppDirectory,
        [Parameter(Mandatory = $true)]
        [string]$MainTenantId,
        [Parameter(Mandatory = $true)]
        [string]$SubscriptionId
    )

    $appInsights = Get-AzApplicationInsights -ResourceGroupName $DataResourceGroup  -Name "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $appInsightsConnectionString = $appInsights.ConnectionString

    $envContent = "REACT_APP_AAD_UI_APP_CLIENT_ID=$UIClientId`n"
    $envContent += "REACT_APP_AAD_APP_TENANT_ID=$UITenantId`n"
    $envContent += "REACT_APP_AAD_API_APP_CLIENT_ID=$WebApiClientId`n"
    $envContent += "REACT_APP_AAD_APP_SERVICE_BASE_URI=$WebApiBaseUri`n"
    $envContent += "REACT_APP_APPINSIGHTS_CONNECTIONSTRING=$appInsightsConnectionString`n"
    $envContent += "REACT_APP_ENVIRONMENT_ABBREVIATION=$EnvironmentAbbreviation`n"
    $envContent += "AZURE_SUBSCRIPTION_ID=$SubscriptionId`n"
    $envContent += "AZURE_TENANT_ID=$MainTenantId`n"

    Set-Content -Path "$WebAppDirectory\.env" -Value $envContent -Force
    $currentLocation = Get-Location

    Set-Location -Path $WebAppDirectory
    swa login --tenant-id $MainTenantId --subscription-id $SubscriptionId
    swa build
    swa deploy "build" --env "Production" -n "$SolutionAbbreviation-ui" -R $ComputeResourceGroup

    Set-Location -Path $currentLocation
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
        [Parameter(Mandatory = $false)]
        [string]$SubscriptionId,
        [Parameter(Mandatory = $true)]
        [string]$TemplateFilesDirectory, # absolute path
        [Parameter(Mandatory = $true)]
        [string]$ParameterFilePath, # absolute path
        [Parameter(Mandatory = $false)]
        [bool]$SkipResourceProvidersCheck = $false,
        [Parameter(Mandatory = $false)]
        [bool]$StartFunctions = $true,
        [Parameter(Mandatory = $false)]
        [bool] $SetUserAssignedManagedIdentityPermissions = $true,
        [Parameter(Mandatory = $false)]
        [System.Nullable[Guid]]$SecondaryTenantId
    )

    # define the resource groups
    $prereqsResourceGroup = "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation"
    $dataResourceGroup = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $computeResourceGroup = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation"
    $resourceGroups = @($prereqsResourceGroup, $dataResourceGroup, $computeResourceGroup)
    $ipAddress = (Invoke-WebRequest -uri “https://api.ipify.org/”).Content

    $scriptsDirectory = Split-Path $PSScriptRoot -Parent

    Set-Subscription `
        -ScriptsDirectory "$scriptsDirectory\Scripts" `
        -SubscriptionId $SubscriptionId

    $context = Get-AzContext

    if (!$SkipResourceProvidersCheck) {
        Set-ResourceProviders
    }

    # Stop-FunctionApps -ResourceGroupName $computeResourceGroup
    Disable-KeyVaultFirewallRules -ResourceGroups $resourceGroups

    $response = Set-GMMResources `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -Location $Location `
        -TemplateFilePath $TemplateFilesDirectory `
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

    $connectionStringADF = Get-AzKeyVaultSecret `
        -VaultName "$SolutionAbbreviation-data-$EnvironmentAbbreviation" `
        -Name "sqlServerBasicConnectionStringADF" `
        -AsPlainText

    Set-SQLServerPermissions `
        -ConnectionString $connectionString `
        -ConnectionStringADF $connectionStringADF `
        -ComputeResourceGroup $computeResourceGroup `
        -DataResourceGroup $dataResourceGroup

    Set-RBACPermissions `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -ScriptsDirectory "$scriptsDirectory\Scripts\PostDeployment" `
        -SetUserAssignedManagedIdentityPermissions $SetUserAssignedManagedIdentityPermissions

    Set-DBMigrations `
        -ConnectionString $connectionString `
        -ScriptsDirectory "$scriptsDirectory\function_packages"

    Set-FunctionAppCode `
        -ComputeResourceGroup $computeResourceGroup `
        -FunctionsPackagesDirectory "$scriptsDirectory\function_packages" `
        -WebApiPackagesDirectory "$scriptsDirectory\webapi_package"


    # Configure web apps
    Set-ConfigureWebApps `
        -WebApiName "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-webapi" `
        -UIWebAppName "$SolutionAbbreviation-ui" `
        -UIAppRegistrationId $response.AppRegistrations.UIApplicationId `
        -ComputeResourceGroup $computeResourceGroup

    # Publish UI code
    Set-PublishUICode `
        -UIClientId $response.AppRegistrations.UIApplicationId `
        -UITenantId $response.AppRegistrations.UITenantId `
        -WebApiClientId $response.AppRegistrations.APIApplicationId `
        -WebApiBaseUri "https://$SolutionAbbreviation-compute-$EnvironmentAbbreviation-webapi.azurewebsites.net" `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -DataResourceGroup $dataResourceGroup `
        -ComputeResourceGroup $computeResourceGroup `
        -WebAppDirectory "$scriptsDirectory\webapp_package\web-app" `
        -MainTenantId $context.Tenant.Id `
        -SubscriptionId $SubscriptionId

    Deploy-PostDeploymentUpdates `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -ScriptsDirectory "$ScriptsDirectory\scripts"

    Set-KeyVaultFirewallRules `
        -ResourceGroups $resourceGroups `
        -ipAddress $ipAddress `
        -ScriptsDirectory "$scriptsDirectory\Scripts" `
        -Region $Location

    if ($StartFunctions) {
        Start-FunctionApps -ResourceGroupName $computeResourceGroup
    }

    # open the web app
    $staticWebApp = Get-AzStaticWebApp -Name "$SolutionAbbreviation-ui" -ResourceGroupName $computeResourceGroup
    if ($null -ne $staticWebApp) {
        Start-Process "https://$($staticWebApp.DefaultHostname)"
    }
}
