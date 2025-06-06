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
Before running this script, make sure you are logged in to Azure and have the necessary permissions to deploy resources.
Connect-AzAccount -TenantId "<tenant-id>"
Set-AzContext -SubscriptionId "<subscription-id>"
az login --tenant "<tenant-id>"
az account set --subscription "<subscription-id>"

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
    foreach ($namespace in @("Microsoft.ServiceBus", "Microsoft.Insights", "Microsoft.OperationalInsights", "Microsoft.AlertsManagement", "Microsoft.Storage", "Microsoft.AppConfiguration", "Microsoft.Sql", "Microsoft.Web", "Microsoft.DataFactory", "Microsoft.SignalRService")) {
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
        $currentUser = Get-AzADUser -SignedIn
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
        $currentUser = Get-AzADUser -SignedIn
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

function Get-DefaultString {
    param(
        [string]$Value,
        $Default
    )
    if ([string]::IsNullOrWhiteSpace($Value)) { return $Default }
    return $Value
}

function Get-Default {
    param(
        [object]$Value,
        [object]$Default
    )
    if ($null -eq $Value) { return $Default }
    return $Value
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
    $parameters = $parameterObject.parameters

    # booleans
    $setRBACPermissions      = Get-Default -Value $parameters['setRBACPermissions'].value      -Default $false
    $createAppRegistrations  = Get-Default -Value $parameters['createAppRegistrations'].value  -Default $true
    $applyDBMigrations       = Get-Default -Value $parameters['applyDBMigrations'].value       -Default $true
    $skipAppRegistrationSetupIfAppExists = Get-Default -Value $parameters['skipAppRegistrationSetupIfAppExists'].value -Default $false

    # strings
    $graphAppCertificateName        = Get-DefaultString -Value $parameters['graphAppCertificateName'].value        -Default 'not-set'
    $teamsChannelAppCertificateName = Get-DefaultString -Value $parameters['teamsChannelAppCertificateName'].value -Default 'not-set'
    $tenantDomain                   = Get-DefaultString -Value $parameters['tenantDomain'].value                   -Default 'not-set'
    $sharepointDomain               = Get-DefaultString -Value $parameters['sharepointDomain'].value               -Default 'not-set'
    $secondaryTenantId              = Get-DefaultString -Value $parameters['secondaryTenantId'].value -Default $null

    $ipAddress = (Invoke-WebRequest -uri "https://api.ipify.org/").Content
    
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

    Start-Sleep -Seconds 10

    Set-KeyVaultFirewallRules `
        -ResourceGroups @($prereqsResourceGroup) `
        -ipAddress $ipAddress `
        -ScriptsDirectory "$scriptsDirectory\Scripts" `
        -Region $Location

    # creating app registrations
    if ($createAppRegistrations -eq $true) {
        Write-Host "`nCreating app registrations"
        $appRegistrations = `
            Set-GMMAppRegistrations `
            -SolutionAbbreviation $SolutionAbbreviation `
            -EnvironmentAbbreviation $EnvironmentAbbreviation `
            -ScriptsDirectory "$scriptsDirectory\Scripts" `
            -SecondaryTenantId $secondaryTenantId `
            -GraphAppCertificateName $graphAppCertificateName `
            -TeamsChannelAppCertificateName $teamsChannelAppCertificateName `
            -TenantDomain $tenantDomain `
            -SharepointDomain $sharepointDomain `
            -SkipAppRegistrationSetupIfAppExists $skipAppRegistrationSetupIfAppExists
    
        # add app registrations to common parameters
        $commonParametersObject.parameters["apiAppClientId"] = @{ "value" = $appRegistrations.APIApplicationId }
        $commonParametersObject.parameters["uiAppTenantId"] = @{ "value" = $appRegistrations.UITenantId }
        $commonParametersObject.parameters["uiAppClientId"] = @{ "value" = $appRegistrations.UIApplicationId }
    
        Start-Sleep -Seconds 10
    }
   
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
    Start-Sleep -Seconds 10

    Set-KeyVaultFirewallRules `
        -ResourceGroups @($dataResourceGroup) `
        -ipAddress $ipAddress `
        -ScriptsDirectory "$scriptsDirectory\Scripts" `
        -Region $Location
    
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

    Start-Sleep -Seconds 10

    # deploy ADF resources
    Write-Host "`nCreating ADF resources"
    $adfResourcesParameters = `
        Get-TemplateParameters `
        -TemplateFilePath "$directoryPath\adfHRResources.json" `
        -ParametersFilePath $ParameterFilePath `
        -AdditionalParameters $commonParametersObject

    $adfDataSecrets = @("sqlAdminPassword", "azureUserReaderUrl", "azureUserReaderKey", "adfStorageAccountName")
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

    Start-Sleep -Seconds 10

    Write-Host "`nResources deployed"

    return @{
        CreateAppRegistrations = $createAppRegistrations
        AppRegistrations = $appRegistrations
        SecondaryTenantId = $secondaryTenantId
        ApplyDBMigrations = $applyDBMigrations
        TenantDomain = $tenantDomain
        SharepointDomain = $sharepointDomain
        SetRBACPermissions = $setRBACPermissions
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
    $functionAppsADF = $functionApps | Where-Object { $_.Name -match "-webapi" -or $_.Name -match "-SqlMembershipObtainer" }

    if ($null -ne $dataFactory) {

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

        $publishCodeOperation = {
            Publish-AzWebApp -ResourceGroupName $ComputeResourceGroup -Name $functionApp.Name -ArchivePath $packageFile -Force
        }

        Retry-Operation `
            -Operation $publishCodeOperation `
            -OperationName "Deploying code for $($functionApp.Name)"
    }

    # publish web api code
    Write-Host "`nPublishing code for webapi app $ComputeResourceGroup-webapi"
    $webApi = Get-AzWebApp -ResourceGroupName $ComputeResourceGroup -Name "$ComputeResourceGroup-webapi"
    $webApiName = $webApi.Name.Split("-")[3]

    $publishWebAPICodeOperation = {
        Publish-AzWebApp -ResourceGroupName $ComputeResourceGroup -Name $webApi.Name -ArchivePath "$WebApiPackagesDirectory\$webApiName.zip" -Force
    }

    Retry-Operation `
        -Operation $publishWebAPICodeOperation `
        -OperationName "Deploying code for $($webApi.Name)"
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

    Write-Host "Enabling firewall rules for key vaults"

    # Get IP rules from script
    . ($ScriptsDirectory + '\Get-FirewallIPRules.ps1') -FolderPathToSaveIpRules $ScriptsDirectory -Regions $Region
    $newIpRules = Get-Content "$ScriptsDirectory\ipRules.txt"
    $newIpRules += $ipAddress

    foreach ($resourceGroup in $ResourceGroups) {
        $keyVaults = Get-AzKeyVault -ResourceGroupName $resourceGroup
        foreach ($keyVault in $keyVaults) {
            $keyVaultName = $keyVault.VaultName
            $keyVaultResourceGroup = $keyVault.ResourceGroupName

            Write-Host "Fetching existing rules for $keyVaultName"
            $detailedKeyVault = Get-AzKeyVault -Name $keyVaultName -ResourceGroupName $keyVaultResourceGroup
            $existingRules = $detailedKeyVault.NetworkAcls.IpAddressRanges

            # Extract current IP rules
            $existingIps = $existingRules.IpRules | ForEach-Object { $_.IpAddress }

            # Combine existing and new, remove duplicates
            $combinedIpRules = ($existingIps + $newIpRules) | Sort-Object -Unique

            Write-Host "Applying updated firewall rules to $keyVaultName"

            Update-AzKeyVaultNetworkRuleSet `
                -VaultName $keyVaultName `
                -ResourceGroupName $keyVaultResourceGroup `
                -Bypass AzureServices `
                -DefaultAction Deny `
                -IpAddressRange $combinedIpRules
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

function Update-AppSettingsVersion {
    param (
        [Parameter(Mandatory = $true)]
        [string]$ComputeResourceGroupName
    )

    Write-Host "`nChecking function app settings"
    $functionApps = Get-AzFunctionApp -ResourceGroupName $ComputeResourceGroupName
    foreach ($function in $functionApps) {

        $settings = Get-AzFunctionAppSetting -ResourceGroupName $ComputeResourceGroupName -Name $function.Name
        foreach ($key in $settings.Keys) {
            if (-not ($settings[$key].Contains("Microsoft.KeyVault"))) {
                continue
            }

            $kvReference = Get-KeyVaultReference -KeyVaultReference $settings[$key]
            $latestSecretVersion = Get-AzKeyVaultSecret -VaultName $kvReference.KeyVaultName -Name $kvReference.SecretName

            if ($latestSecretVersion.Version -ne $kvReference.Version) {
                Write-Host "Updating $($function.Name) -> $($kvReference.SecretName) to $($latestSecretVersion.Version)"
                $updatedVersion = $settings[$key] -replace $kvReference.Version, $latestSecretVersion.Version
                $updatedSettings = Update-AzFunctionAppSetting -Name $function.Name -ResourceGroupName $ComputeResourceGroupName -AppSetting @{$key = $updatedVersion }
            }
        }
    }

    Write-Host "`nChecking web app settings"
    $webApps = Get-AzWebApp -ResourceGroupName $ComputeResourceGroupName | Where-Object { $_.Kind -eq "app" }
    foreach ($webApp in $webApps) {

        $hasUpdates = $false

        foreach ($keyPair in $webApp.SiteConfig.AppSettings) {

            $key = $keyPair.Name
            $value = $keyPair.Value

            if (-not ($value.Contains("Microsoft.KeyVault"))) {
                continue
            }

            $kvReference = Get-KeyVaultReference -KeyVaultReference $value
            $latestSecretVersion = Get-AzKeyVaultSecret -VaultName $kvReference.KeyVaultName -Name $kvReference.SecretName

            if ($latestSecretVersion.Version -ne $kvReference.Version) {
                Write-Host "Updating $($webApp.Name) -> $key to $($latestSecretVersion.Version)"
                $updatedVersion = $value -replace $kvReference.Version, $latestSecretVersion.Version

                $appSettings = $webApp.SiteConfig.AppSettings
                $settingsCount = $appSettings.Count
                for ($i = 0; $i -lt $settingsCount; $i++) {
                    if ($appSettings[$i].Name -eq $key) {
                        $appSettings[$i].Value = $updatedVersion
                        break
                    }
                }

                $hasUpdates = $true
            }
        }

        if ($hasUpdates) {
            $updatedSettings = @{}
            foreach ($setting in $webApp.SiteConfig.AppSettings) {
                $updatedSettings[$setting.Name] = $setting.Value
            }

            Set-AzWebApp -ResourceGroupName $ComputeResourceGroupName -Name $webApp.Name -AppSettings $updatedSettings
        }

    }
}

function  Get-KeyVaultReference {
    param (
        [Parameter(Mandatory = $true)]
        [string]$KeyVaultReference
    )

    $keyVaultNamePattern = "SecretUri=https://(?<kvName>.*?)\.vault"
    $kvMatch = [regex]::Match($KeyVaultReference, $keyVaultNamePattern)
    $kvName = $kvMatch.Groups["kvName"].Value

    $secretNamePattern = "secrets/(?<secret>.*?)/"
    $secretNameMatch = [regex]::Match($KeyVaultReference, $secretNamePattern)
    $secretName = $secretNameMatch.Groups["secret"].Value

    $pattern = "$secretName/(?<version>.*)\)"
    $match = [regex]::Match($KeyVaultReference, $pattern)
    $version = $match.Groups["version"].Value

    return @{
        KeyVaultName = $kvName;
        SecretName   = $secretName;
        Version      = $version;
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
        [string] $GraphAppCertificateName,
        [Parameter(Mandatory = $False)]
        [string] $TeamsChannelAppCertificateName,
        [Parameter(Mandatory = $False)]
        [string] $TenantDomain,
        [Parameter(Mandatory = $False)]
        [boolean] $SkipAppRegistrationSetupIfAppExists = $false,
        [Parameter(Mandatory = $False)]
        [string] $SharepointDomain
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
        -TenantDomain $TenantDomain `
        -SharepointDomain $SharepointDomain `
        -SaveToKeyVault $true `
        -SkipPrompts $true `
        -SkipIfApplicationExists $SkipAppRegistrationSetupIfAppExists `
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
        -SkipIfApplicationExists $SkipAppRegistrationSetupIfAppExists `
        -Clean $false

    . ($ScriptsDirectory + '\Set-GraphCredentialsAzureADApplication.ps1')
    $graphInformation = Set-GraphCredentialsAzureADApplication `
        -SubscriptionName $subscriptionName `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -TenantIdToCreateAppIn (Get-Default -Value $SecondaryTenantId -Default $mainTenantId) `
        -TenantIdWithKeyVault $mainTenantId `
        -SaveToKeyVault $true `
        -SkipPrompts $true `
        -SkipIfApplicationExists $SkipAppRegistrationSetupIfAppExists `
        -CertificateName $GraphAppCertificateName `
        -Clean $false

    . ($ScriptsDirectory + '\Set-TeamsChannelAzureADApplication.ps1')
    $teamsChannelInformation = Set-TeamsChannelAzureADApplication `
        -SubscriptionName $subscriptionName `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -TenantIdToCreateAppIn (Get-Default -Value $SecondaryTenantId -Default $mainTenantId) `
        -TenantIdWithKeyVault $mainTenantId `
        -SaveToKeyVault $true `
        -SkipPrompts $true `
        -SkipIfApplicationExists $SkipAppRegistrationSetupIfAppExists `
        -CertificateName $TeamsChannelCertificateName `
        -Clean $false

    $null = Set-AzContext -Tenant $mainTenantId -Subscription $subscriptionName

    return @{
        UIApplicationId            = $uiInformation.ApplicationId;
        UITenantId                 = $uiInformation.TenantId;
        APIApplicationId           = $apiInformation.ApplicationId;
        APITenantId                = $apiInformation.TenantId;
        GraphApplicationId         = $graphInformation.ApplicationId;
        GraphTenantId              = $graphInformation.TenantId;
        TeamsChannelApplicationId  = $teamsChannelInformation.ApplicationId;
        TeamsChannelTenantId       = $teamsChannelInformation.TenantId;
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
        [Parameter(Mandatory = $False)]
        [System.Nullable[Guid]] $DevTenantId,
        [Parameter(Mandatory = $true)]
        [string]$ComputeResourceGroup
    )

    $currentContext = Get-AzContext
    $mainTenantId = $currentContext.Tenant.Id

    # Set CORS for web apps
    $allowedOrigins = @()

    try {
        $customDomain = Get-AzStaticWebAppCustomDomain -Name $UIWebAppName -ResourceGroupName $ComputeResourceGroup
        if (-not [string]::IsNullOrEmpty($customDomain)) {
            $allowedOrigins += "https://$($customDomain.DomainName)"
        }
    }
    catch {
        Write-Output "No custom domain associated with this web app."
    }

    $staticWebApp = Get-AzStaticWebApp -Name $UIWebAppName -ResourceGroupName $ComputeResourceGroup
    $allowedOrigins += "https://$($staticWebApp.DefaultHostname)"

    # Set CORS for SignalR service
    try {
        Update-AzSignalR `
            -ResourceGroupName $ComputeResourceGroup `
            -Name "$ComputeResourceGroup-signalr" `
            -AllowedOrigin $allowedOrigins
    }
    catch {
        Write-Output "Unable to update SignalR service CORS settings."
    }

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
	if(-not [string]::IsNullOrWhiteSpace($DevTenantId) -and $mainTenantId -ne $DevTenantId) {
        Write-Host "Please sign in to your dev tenant."
		Connect-AzAccount -Tenant $DevTenantId
	}

    $uiApp = Get-AzADApplication -ApplicationId $UIAppRegistrationId
    $currentRedirectUris = Get-Default -Value $uiApp.Spa.RedirectUri -Default @()
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

    if(-not [string]::IsNullOrWhiteSpace($DevTenantId) -and $mainTenantId -ne $DevTenantId) {
        Write-Host "Please sign in to your main tenant."
		Connect-AzAccount -Tenant $mainTenantId
	}
}

function Set-PublishUICode {
    param (
        [Parameter(Mandatory = $false)]
        [string]$UIClientId,
        [Parameter(Mandatory = $false)]
        [string]$UITenantId,
        [Parameter(Mandatory = $false)]
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
        [string]$TenantDomain,
        [Parameter(Mandatory = $true)]
        [string]$SharepointDomain,
        [Parameter(Mandatory = $true)]
        [string]$SubscriptionId
    )


    if ([string]::IsNullOrWhiteSpace($UIClientId)) {
        $UIClientId = Get-AzKeyVaultSecret `
                        -VaultName "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation" `
                        -Name "uiAppId" `
                        -AsPlainText
    } 

    if ([string]::IsNullOrWhiteSpace($UITenantId)) {
        $UITenantId = Get-AzKeyVaultSecret `
                        -VaultName "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation" `
                        -Name "uiTenantId" `
                        -AsPlainText
    } 

    if ([string]::IsNullOrWhiteSpace($WebApiClientId)) {
        $WebApiClientId = Get-AzKeyVaultSecret `
                            -VaultName "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation" `
                            -Name "webApiClientId" `
                            -AsPlainText
    } 
    
    $appInsights = Get-AzApplicationInsights -ResourceGroupName $DataResourceGroup  -Name "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $appInsightsConnectionString = $appInsights.ConnectionString

    $buildVersionFilePath = "$WebAppDirectory\buildVersion.txt"
    if (Test-Path -Path $buildVersionFilePath) {
        $buildVersion = Get-Content -Path $buildVersionFilePath
        Write-Host "Build version: $buildVersion"
    }

    $envContent = "REACT_APP_AAD_UI_APP_CLIENT_ID=$UIClientId`n"
    $envContent += "REACT_APP_AAD_APP_TENANT_ID=$UITenantId`n"
    $envContent += "REACT_APP_AAD_API_APP_CLIENT_ID=$WebApiClientId`n"
    $envContent += "REACT_APP_AAD_APP_SERVICE_BASE_URI=$WebApiBaseUri`n"
    $envContent += "REACT_APP_APPINSIGHTS_CONNECTIONSTRING=$appInsightsConnectionString`n"
    $envContent += "REACT_APP_ENVIRONMENT_ABBREVIATION=$EnvironmentAbbreviation`n"
    $envContent += "REACT_APP_SHAREPOINTDOMAIN=$SharepointDomain`n"
    $envContent += "REACT_APP_DOMAINNAME=$TenantDomain`n"
    $envContent += "AZURE_SUBSCRIPTION_ID=$SubscriptionId`n"
    $envContent += "AZURE_TENANT_ID=$MainTenantId`n"
    $envContent += "REACT_APP_VERSION_NUMBER=$buildVersion`n"
    $envContent += "DISABLE_ESLINT_PLUGIN=true`n"

    Set-Content -Path "$WebAppDirectory\.env" -Value $envContent -Force
    $currentLocation = Get-Location

    Set-Location -Path $WebAppDirectory

    # Get the web app deployment token
    $webAppName = "$SolutionAbbreviation-ui"
    $webAppSecrets = (Get-AzStaticWebAppSecret -name $webAppName -ResourceGroupName $ComputeResourceGroup).Property | ConvertFrom-Json
    $webAppDeploymentToken = $webAppSecrets.apiKey

    swa build
    swa deploy "build" --env "Production" -n $webAppName -R $ComputeResourceGroup --deployment-token $webAppDeploymentToken

    Set-Location -Path $currentLocation
}

function Test-ScriptDependencies {
    $dependenciesPresent = $true
    Write-Host "🔍 Checking required dependencies..."

    # PowerShell Core
    if ($PSVersionTable.PSEdition -ne "Core") {
        Write-Error "❌ This script requires PowerShell Core (pwsh). Current edition: $($PSVersionTable.PSEdition)"
        exit 1
    } else {
        Write-Host "✅ Running on PowerShell Core version $($PSVersionTable.PSVersion)" -ForegroundColor Green
    }

    # 64-bit
    if (-not [Environment]::Is64BitProcess) {
        Write-Error "❌ This script must be run in a 64-bit PowerShell session."
        exit 1
    } else {
        Write-Host "✅ Running in a 64-bit PowerShell session." -ForegroundColor Green
    }

    # Node.js
    if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
        Write-Error "❌ Node.js is not installed. Download it from https://nodejs.org/."
        $dependenciesPresent = $false
    } else {
        Write-Host "✅ Node.js $((node -v).Trim()) is installed." -ForegroundColor Green
    }

    # npm
    if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
        Write-Error "❌ npm is not installed. It should be included with Node.js."
        $dependenciesPresent = $false
    } else {
        Write-Host "✅ npm $((npm -v).Trim()) is installed." -ForegroundColor Green
    }

    # pnpm
    if (-not (Get-Command pnpm -ErrorAction SilentlyContinue)) {
        Write-Warning "⚠️ pnpm is not installed. Attempting to install..."
        npm install -g pnpm

        if ($LASTEXITCODE -ne 0) {
            Write-Error "❌ pnpm installation failed."
            exit 1
        }

        Write-Host "✅ pnpm installed successfully." -ForegroundColor Green
    } else {
        Write-Host "✅ pnpm is already installed." -ForegroundColor Green
    }

    # swa CLI
    $desiredVersion = "2.0.5"
    $swaInstalled = Get-Command swa -ErrorAction SilentlyContinue

    if ($swaInstalled) {
        $installedVersion = (npm list -g @azure/static-web-apps-cli --depth=0 | 
            Select-String -Pattern "@azure/static-web-apps-cli@([\d\.]+)" | 
            ForEach-Object { $_.Matches[0].Groups[1].Value })

        if ($installedVersion -eq $desiredVersion) {
            Write-Host "✅ swa version $desiredVersion is already installed." -ForegroundColor Green
        } else {
            Write-Warning "⚠️ swa version is $installedVersion, expected $desiredVersion. Updating..."
            npm install -g @azure/static-web-apps-cli@$desiredVersion
        }
    } else {
        Write-Host "📦 Installing swa CLI version $desiredVersion..."
        npm install -g @azure/static-web-apps-cli@$desiredVersion
    }

    # Confirm swa version installed
    $installedVersion = (npm list -g @azure/static-web-apps-cli --depth=0 | 
        Select-String -Pattern "@azure/static-web-apps-cli@([\d\.]+)" | 
        ForEach-Object { $_.Matches[0].Groups[1].Value })

    if ($installedVersion -eq $desiredVersion) {
        Write-Host "✅ swa version $desiredVersion installed successfully." -ForegroundColor Green
    } else {
        Write-Error "❌ Failed to install swa version $desiredVersion."
        exit 1
    }

    # Required paths and files
    $scriptsDirectory = Split-Path $PSScriptRoot -Parent
    $requiredPaths = @{
        "function_packages"        = "$scriptsDirectory\function_packages"
        "webapi_package"          = "$scriptsDirectory\webapi_package"
        "webapp_package\web-app"  = "$scriptsDirectory\webapp_package\web-app"
        "Scripts"                 = "$scriptsDirectory\Scripts"
        "Scripts\PostDeployment"  = "$scriptsDirectory\Scripts\PostDeployment"
        "efbundle.exe"            = "$scriptsDirectory\function_packages\efbundle.exe"
    }

    foreach ($item in $requiredPaths.GetEnumerator()) {
        $pathType = if ($item.Key -eq "efbundle.exe") { "Leaf" } else { "Container" }
        if (-not (Test-Path -Path $item.Value -PathType $pathType)) {
            Write-Error "❌ Missing: $($item.Key) at $($item.Value)"
            $dependenciesPresent = $false
        } else {
            Write-Host "✅ Found $($item.Key)." -ForegroundColor Green
        }
    }

    if (-not $dependenciesPresent) {
        Write-Error "❌ One or more dependencies are missing. Please resolve them before continuing."
        exit 1
    }

    Write-Host "🎉 All dependencies verified successfully!" -ForegroundColor Green
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

    Test-ScriptDependencies

    # define the resource groups
    $dataResourceGroup = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $computeResourceGroup = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation"
    $ipAddress = (Invoke-WebRequest -uri "https://api.ipify.org/").Content

    $scriptsDirectory = Split-Path $PSScriptRoot -Parent

    Set-Subscription `
        -ScriptsDirectory "$scriptsDirectory\Scripts" `
        -SubscriptionId $SubscriptionId

    $context = Get-AzContext

    if (!$SkipResourceProvidersCheck) {
        Set-ResourceProviders
    }

    # Stop-FunctionApps -ResourceGroupName $computeResourceGroup

    $response = Set-GMMResources `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -Location $Location `
        -TemplateFilePath $TemplateFilesDirectory `
        -ParameterFilePath $ParameterFilePath

    Start-Sleep -Seconds 30

    Update-AppSettingsVersion -ComputeResourceGroupName $computeResourceGroup


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
        -Name "sqlServerBasicConnectionString" `
        -AsPlainText

    Set-SQLServerPermissions `
        -ConnectionString $connectionString `
        -ConnectionStringADF $connectionStringADF `
        -ComputeResourceGroup $computeResourceGroup `
        -DataResourceGroup $dataResourceGroup

    if ($true -eq $response.SetRBACPermissions) {
        Set-RBACPermissions `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -ScriptsDirectory "$scriptsDirectory\Scripts\PostDeployment" `
        -SetUserAssignedManagedIdentityPermissions $SetUserAssignedManagedIdentityPermissions
    }
    
    if ($true -eq $response.ApplyDBMigrations) {
        Set-DBMigrations `
            -ConnectionString $connectionString `
            -ScriptsDirectory "$scriptsDirectory\function_packages"
    }

    Set-FunctionAppCode `
        -ComputeResourceGroup $computeResourceGroup `
        -FunctionsPackagesDirectory "$scriptsDirectory\function_packages" `
        -WebApiPackagesDirectory "$scriptsDirectory\webapi_package"

    # Configure web apps
    if ($true -eq $response.CreateAppRegistrations) {
        Set-ConfigureWebApps `
            -WebApiName "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-webapi" `
            -UIWebAppName "$SolutionAbbreviation-ui" `
            -DevTenantId $response.SecondaryTenantId `
            -UIAppRegistrationId $response.AppRegistrations.UIApplicationId `
            -ComputeResourceGroup $computeResourceGroup
    }

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
        -TenantDomain $response.TenantDomain `
        -SharepointDomain $response.SharepointDomain `
        -SubscriptionId $SubscriptionId

    Deploy-PostDeploymentUpdates `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -ScriptsDirectory "$ScriptsDirectory\scripts"
        

    if ($StartFunctions) {
        Start-FunctionApps -ResourceGroupName $computeResourceGroup
    }

    # open the web app
    $staticWebApp = Get-AzStaticWebApp -Name "$SolutionAbbreviation-ui" -ResourceGroupName $computeResourceGroup
    if ($null -ne $staticWebApp) {
        Write-Host "`nOpening UI in browser, url: https://$($staticWebApp.DefaultHostname)"
        Start-Process "https://$($staticWebApp.DefaultHostname)"
    }
}