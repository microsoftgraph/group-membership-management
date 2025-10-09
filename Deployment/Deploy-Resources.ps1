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

Default: 

Deploy-Resources

If you want to specify a different parameter file name, use the following syntax:

Deploy-Resources -parameterFileName "<FILE_NAME>"

#>

$maxRetries = 6

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

            $retryWaitSeconds = 20 * $retryCount
            Write-Warning "'$OperationName' failed, retrying again in $retryWaitSeconds seconds... Retry attempt ($retryCount/$maxRetries)"
            Start-Sleep -Seconds $retryWaitSeconds
        }
    } while ($true)
}

function Set-PostDeploymentUpdates {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$ScriptsDirectory,
        [Parameter(Mandatory = $true)]
        [string]$ConnectionString
    )

    . ($ScriptsDirectory + '/PostDeploymentMigrations/Set-PostDeploymentMigrations.ps1')
    $currentContext = Get-AzContext
    Set-PostDeploymentMigrations `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -SolutionAbbreviation $SolutionAbbreviation `
        -SubscriptionName $currentContext.Subscription.Name `
        -ConnectionString $ConnectionString
}

function Set-PreDeploymentUpdates {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$ScriptsDirectory,
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $false)]
        [string]$SyncJobsDBConnectionString,
        [Parameter(Mandatory = $false)]
        [string]$ADFDBConnectionString
    )

    . ($ScriptsDirectory + '/PreDeploymentMigrations/Set-PreDeploymentMigrations.ps1')

    Set-PreDeploymentMigrations `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -SyncJobsDBConnectionString $SyncJobsDBConnectionString `
        -ADFDBConnectionString $ADFDBConnectionString
}

function Set-Subscription {
    param (
        [Parameter(Mandatory = $false)]
        [string]$SubscriptionId,
        [Parameter(Mandatory = $true)]
        [string]$ScriptsDirectory
    )

    if (-not $SubscriptionId) {
        Write-Host "`nCurrent subscription:`n"
        $currentSubscription = (Get-AzContext).Subscription
        Write-Host "$($currentSubscription.Name) -  $($currentSubscription.Id)"
        Write-Host "`n"
        $SubscriptionId = Read-Host -Prompt "If you would like to use other subscription than '$($currentSubscription.Name)' `nprovide the subscription id, otherwise press enter to continue."
    }

    if ($SubscriptionId) {
        try {
            Set-AzContext -SubscriptionId $SubscriptionId -ErrorAction Stop
            $currentSubscription = (Get-AzContext).Subscription
            Write-Host "`n✅ Selected subscription: $($currentSubscription.Name) - $($currentSubscription.Id)"
        }
        catch {
            Write-Host "`n❌ Failed to set subscription context." -ForegroundColor Red
            Write-Host "   SubscriptionId: $SubscriptionId"
            Write-Host "   TenantId:       $((Get-AzContext).Tenant.Id)"
            Write-Host "   Account:        $((Get-AzContext).Account)"
            Write-Host "   Error:          $($_.Exception.Message)" -ForegroundColor Yellow

            If ($_.Exception.Message -match "Please provide a valid tenant or a valid subscription.") {
                Write-Host "`nThis issue is sometimes caused by the user account not having any RBAC permissions on the subscription.`n" -ForegroundColor Yellow
            }

            throw
        }
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
        [Hashtable]$ParameterHashtable,
        [Parameter(Mandatory = $false)]
        [Hashtable]$AdditionalParameters = @{ parameters = @{} }
    )

    $TemplateObject = Get-TemplateAsHashtable -TemplateFilePath $TemplateFilePath
    $commonParametersObject = @{}

    # add those with a default value
    $TemplateObject.parameters.Keys | ForEach-Object {
        $parameter = $TemplateObject.parameters[$_]
        if ($parameter.Keys -contains "defaultValue") {
            $commonParametersObject[$_] = @{ value = $parameter.defaultValue }
        }
    }

    # add from the additional parameters
    if ($AdditionalParameters.parameters.Keys.Count -gt 0) {
        $TemplateObject.parameters.Keys | ForEach-Object {
            if ($AdditionalParameters.parameters.Keys -contains $_) {
                $commonParametersObject[$_] = @{ value = $AdditionalParameters.parameters[$_].value }
            }
        }
    }

    # add (or overwrite) from the parameters file
    $TemplateObject.parameters.Keys | ForEach-Object {
        if ($ParameterHashtable.Keys -contains $_) {
            $commonParametersObject[$_] = @{ value = $ParameterHashtable[$_].value }
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

    $secret = Get-KeyVaultSecretWithFirewallRetry -ResourceGroup $VaultName -VaultName $VaultName -SecretName $SecretName -AsPlainText -ErrorAction SilentlyContinue
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

function Get-BearerToken {
    param (
        [string]$Resource = "https://management.azure.com/"
    )

    $token = (Get-AzAccessToken -ResourceUrl $Resource).Token

    if ($token -is [System.Security.SecureString]) {
        $ptr = [Runtime.InteropServices.Marshal]::SecureStringToGlobalAllocUnicode($token)
        try {
            return [Runtime.InteropServices.Marshal]::PtrToStringUni($ptr)
        }
        finally {
            [Runtime.InteropServices.Marshal]::ZeroFreeGlobalAllocUnicode($ptr)
        }
    }
    elseif ($token -is [string]) {
        return $token
    }
    else {
        throw "Unexpected token type: $($token.GetType().FullName)"
    }
}

function Start-ResourceDeployment {
    param (
        [Parameter(Mandatory = $true)][string]$SubscriptionId,
        [Parameter(Mandatory = $true)][string]$TemplateFilePath,
        [Parameter(Mandatory = $true)][Hashtable]$ParameterHashtable,
        [Parameter(Mandatory = $false)][string]$ResourceGroupName,
        [Parameter(Mandatory = $false)][boolean]$IsResourceGroupCreation = $false,
        [Parameter(Mandatory = $false)][string]$Location,
        [Parameter(Mandatory = $false)][Hashtable]$AdditionalParameters = @{ parameters = @{} }
    )

    if ($IsResourceGroupCreation -eq $false -and [string]::IsNullOrWhiteSpace($ResourceGroupName)) {
        throw "Start-ResourceDeployment: Parameter ResourceGroupName is required when parameter IsResourceGroupCreation is set to false."
    }
    elseif ($IsResourceGroupCreation -eq $true -and [string]::IsNullOrWhiteSpace($Location)) {
        throw "Start-ResourceDeployment: Parameter Location is required when parameter IsResourceGroupCreation is set to true."
    }

    if (-not $IsResourceGroupCreation) {
        Write-Host "Starting REST deployment to resource group: $ResourceGroupName"
    }
    else {
        Write-Host "Starting REST deployment of resource groups."
    }
    
    Write-Host "Using template file: $TemplateFilePath"

    if (-not (Test-Path $TemplateFilePath)) { throw "Template file not found at path: $TemplateFilePath" }

    $deploymentName = "deployment-$(Get-Date -Format yyyyMMddHHmmss)"
    $templateContent = Get-TemplateAsHashtable -TemplateFilePath $TemplateFilePath
    $templateParameters = Get-TemplateParameters `
        -TemplateFilePath $TemplateFilePath `
        -ParameterHashtable $ParameterHashtable `
        -AdditionalParameters $AdditionalParameters

    $body = ""
    $baseUri = ""

    if ($IsResourceGroupCreation) {
        $body = @{
            location = $Location
            properties = @{
                mode = 'Incremental'
                template = $templateContent
                parameters = $templateParameters
            }
        } | ConvertTo-Json -Depth 100
        $baseUri = "https://management.azure.com/subscriptions/$SubscriptionId/providers/Microsoft.Resources/deployments/$deploymentName"
    }
    else {
        $body = @{
            properties = @{
                mode = 'Incremental'
                template = $templateContent
                parameters = $templateParameters
            }
        } | ConvertTo-Json -Depth 100
        $baseUri = "https://management.azure.com/subscriptions/$SubscriptionId/resourcegroups/$ResourceGroupName/providers/Microsoft.Resources/deployments/$deploymentName"
    }

    $uri = "$($baseUri)?api-version=2025-03-01"
    $token = Get-BearerToken
    $headers = @{
        Authorization = "Bearer $token"
        'Content-Type' = 'application/json'
    }

    try {
        Write-Host "Invoking deployment via REST API..."
        $initialResponse = Invoke-RestMethod -Uri $uri -Method Put -Headers $headers -Body $body

        $maxAttempts = 100
        $delaySeconds = 15
        $attempt = 0
        $provisioningState = $initialResponse.properties.provisioningState

        while ($provisioningState -in @("Accepted", "Running", "InProgress")) {
            Start-Sleep -Seconds $delaySeconds
            $attempt++

            Write-Host "Polling deployment status (Attempt $attempt/$maxAttempts)..."
            $statusResponse = Invoke-RestMethod -Uri $uri -Method Get -Headers $headers
            $provisioningState = $statusResponse.properties.provisioningState
            Write-Host "Current state: $provisioningState"

            if ($attempt -ge $maxAttempts) {
                throw "Deployment status check timed out after $maxAttempts attempts."
            }
        }

        if ($provisioningState -ne "Succeeded") {
            Write-Host "`n❌ Deployment failed. Final state: $provisioningState"

            # Log top-level error
            if ($statusResponse.properties.error) {
                Write-Host "`n🔸 Top-Level Error details:"
                Write-Host "    Error Code: $($statusResponse.properties.error.code)"
                Write-Host "    Error Message: $($statusResponse.properties.error.message)"
            }

            # Fetch deployment operations
            $opsUri = "$baseUri/operations?api-version=2025-03-01"
            $opsResponse = Invoke-RestMethod -Uri $opsUri -Method Get -Headers $headers

            $opsResponse.value | Where-Object { $_.properties.provisioningState -eq 'Failed' } | ForEach-Object {
                Write-Host "`n🔸🔸 Failed operation: $($_.properties.targetResource.resourceName)"
                Write-Host "        - Type: $($_.properties.targetResource.resourceType)"
                Write-Host "        - Status: $($_.properties.provisioningState)"
                Write-Host "        - Error: $($_.properties.statusMessage.error.message)"
                $_.properties.statusMessage.error.details | ForEach-Object {
                    Write-Host "           - Error Detail:"
                    Write-Host "               - Code: $($_.code)"
                    Write-Host "               - Message: $($_.message)"
                }
            }

            throw "Deployment failed. See logs above."
        }

        Write-Host "`n✅ Deployment succeeded."
        return $statusResponse
    }
    catch {
        Write-Error "`n❌ Deployment failed unexpectedly: $_"
        throw
    }
    finally {
       # Clear sensitive token from memory
        $token = $null
        $headers = $null
    }
}

function Set-ResourceGroups {
    param (
        [Parameter(Mandatory = $true)]
        [string]$SubscriptionId,
        [Parameter(Mandatory = $true)]
        [string]$Location,
        [Parameter(Mandatory = $true)]
        [string]$ResourceGroupTemplateDirectoryPath,
        [Parameter(Mandatory = $true)]
        [Hashtable]$ParameterHashtable,
        [Parameter(Mandatory = $false)]
        [Hashtable]$AdditionalParameters = @{ parameters = @{} },
        [Parameter(Mandatory = $false)]
        [bool] $SetRBACPermissions
    )
    
    Write-Host "`nCreating resource groups:"
    $templateFilePath = "$ResourceGroupTemplateDirectoryPath/resourceGroups.json"
    Retry-Operation `
        -Operation ${function:Start-ResourceDeployment} `
        -OperationName "Create Resource Groups" `
        -params @{
        SubscriptionId          = $SubscriptionId
        Location                = $Location
        TemplateFilePath        = $templateFilePath
        ParameterHashtable      = $ParameterHashtable
        AdditionalParameters    = $AdditionalParameters
        IsResourceGroupCreation = $true
    }
}

function Set-PrereqResources {
    param (
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$SubscriptionId,
        [Parameter(Mandatory = $true)]
        [string]$PrereqsTemplateDirectoryPath,
        [Parameter(Mandatory = $true)]
        [Hashtable]$ParameterHashtable,
        [Parameter(Mandatory = $false)]
        [Hashtable]$AdditionalParameters = @{ parameters = @{} },
        [Parameter(Mandatory = $false)]
        [bool] $SetRBACPermissions
    )

    Write-Host "`nCreating prereqs resources"
    $prereqsResourceGroup = "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation"
    $templateFilePath = "$PrereqsTemplateDirectoryPath/prereqResources.json"
    Retry-Operation `
        -Operation ${function:Start-ResourceDeployment} `
        -OperationName "Create prereqs resources" `
        -params @{
        ResourceGroupName       = $prereqsResourceGroup
        SubscriptionId          = $SubscriptionId
        TemplateFilePath        = $templateFilePath
        ParameterHashtable      = $ParameterHashtable
        AdditionalParameters    = $AdditionalParameters
    }

    # grant permissions to prereqs key vault
    if ($SetRBACPermissions -eq $true) {
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
        [string]$SubscriptionId,
        [Parameter(Mandatory = $true)]
        [string]$DataTemplateDirectoryPath,
        [Parameter(Mandatory = $true)]
        [Hashtable]$ParameterHashtable,
        [Parameter(Mandatory = $false)]
        [Hashtable]$AdditionalParameters = @{ parameters = @{} },
        [Parameter(Mandatory = $false)]
        [bool] $SetRBACPermissions
    )
    
    Write-Host "`nCreating data resources"
    $dataResourceGroup = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $templateFilePath = "$DataTemplateDirectoryPath/dataResources.json"
    Retry-Operation `
        -Operation ${function:Start-ResourceDeployment} `
        -OperationName "Create data resources" `
        -params @{
        ResourceGroupName       = $dataResourceGroup
        SubscriptionId          = $SubscriptionId
        TemplateFilePath        = $templateFilePath
        ParameterHashtable      = $ParameterHashtable
        AdditionalParameters    = $AdditionalParameters
    }

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
        [string]$SubscriptionId,
        [Parameter(Mandatory = $true)]
        [string]$ComputeTemplateDirectoryPath,
        [Parameter(Mandatory = $true)]
        [Hashtable]$ParameterHashtable,
        [Parameter(Mandatory = $false)]
        [Hashtable]$AdditionalParameters = @{ parameters = @{} }
    )

    write-Host "`nEnsuring secrets are set in the Key Vault"
    $parameterObject = Get-TemplateAsHashtable -TemplateFilePath $ParameterFilePath
    $parameters = $parameterObject.parameters
    $storageAccountSecretName  = Get-DefaultString -Value $parameters['storageAccountSecretName'].value -Default "adfStorageAccountName"

    $dataResourceGroup = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $secrets = @("sqlServerMSIConnectionString", $storageAccountSecretName)
    Set-DefaultSecretsIfMissing `
        -KeyVaultName $dataResourceGroup `
        -SecretNames $secrets

    Write-Host "`nCreating compute resources"
    $computeResourceGroup = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation"
    $templateFilePath = "$ComputeTemplateDirectoryPath/computeResources.json"
    Retry-Operation `
        -Operation ${function:Start-ResourceDeployment} `
        -OperationName "Create compute resources" `
        -params @{
        ResourceGroupName       = $computeResourceGroup
        SubscriptionId          = $SubscriptionId
        TemplateFilePath        = $templateFilePath
        ParameterHashtable      = $ParameterHashtable
        AdditionalParameters    = $AdditionalParameters
    }
}

function Set-ADFResources {
    param (
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$SubscriptionId,
        [Parameter(Mandatory = $true)]
        [string]$ADFTemplateDirectoryPath,
        [Parameter(Mandatory = $true)]
        [Hashtable]$ParameterHashtable,
        [Parameter(Mandatory = $false)]
        [Hashtable]$AdditionalParameters = @{ parameters = @{} }
    )

    $dataResourceGroup = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"

    # Ensure ADF secrets are set in the Key Vault
    write-Host "`nEnsuring ADF secrets are set in the Key Vault"
    $adfDataSecrets = @("azureUserReaderUrl", "azureUserReaderKey", "adfStorageAccountName")
    Set-DefaultSecretsIfMissing `
        -KeyVaultName $dataResourceGroup `
        -SecretNames $adfDataSecrets

    # Deploy ADF resources
    Write-Host "`nCreating ADF resources"
    $templateFilePath = "$ADFTemplateDirectoryPath/adfHRResources.json"
    Retry-Operation `
        -Operation ${function:Start-ResourceDeployment} `
        -OperationName "Create ADF resources" `
        -params @{
        ResourceGroupName       = $dataResourceGroup
        SubscriptionId          = $SubscriptionId
        TemplateFilePath        = $templateFilePath
        ParameterHashtable      = $ParameterHashtable
        AdditionalParameters    = $AdditionalParameters
    }
}

function Set-DefaultSecretsIfMissing {
    param (
        [Parameter(Mandatory = $true)]
        [string]$KeyVaultName,
        [Parameter(Mandatory = $true)]
        [string[]]$SecretNames
    )

    foreach ($secretName in $SecretNames) {
        $secretExists = Check-IfKeyVaultSecretExists -VaultName $KeyVaultName -SecretName $secretName
        if (-not $secretExists) {
            $secretValue = New-Object System.Security.SecureString
            "not-set".ToCharArray() | ForEach-Object { $secretValue.AppendChar($_) }
            Set-KeyVaultSecretWithFirewallRetry -VaultName $KeyVaultName -ResourceGroup $KeyVaultName -SecretName $secretName -SecretValue $secretValue
        }
    }
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

function Get-CommonParameters {
    param (
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation
    )

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

    return $commonParametersObject
}

function Set-GMMResources {
    param (
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$SubscriptionId,
        [Parameter(Mandatory = $true)]
        [string]$Location,
        [Parameter(Mandatory = $true)]
        [string]$TemplateFilesDirectory,
        [Parameter(Mandatory = $true)]
        [string]$ScriptsDirectory,
        [Parameter(Mandatory = $true)]
        [hashtable]$ParameterHashtable
    )

    # deploy resources
    Write-Host "`nDeploying resources"

    $prereqsResourceGroup = "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation"
    $dataResourceGroup = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"

    $commonParametersObject = Get-CommonParameters `
                                -SolutionAbbreviation $SolutionAbbreviation `
                                -EnvironmentAbbreviation $EnvironmentAbbreviation

    $setRBACPermissions      = Get-Default -Value $ParameterHashtable['setRBACPermissions'].value      -Default $false
    $createAppRegistrations  = Get-Default -Value $ParameterHashtable['createAppRegistrations'].value  -Default $true
    $skipAppRegistrationSetupIfAppExists = Get-Default -Value $ParameterHashtable['skipAppRegistrationSetupIfAppExists'].value -Default $false
    $setRBACPermissionsBicep = Get-Default -Value $ParameterHashtable['setRBACPermissionsBicep'].value -Default $false
    $createResourceGroups = Get-Default -Value $ParameterHashtable['createResourceGroups'].value -Default $false
    $skipAzureDataFactoryDeployment = Get-Default -Value $ParameterHashtable['skipAzureDataFactoryDeployment'].value -Default $false
    $ipRangesToWhiteList = Get-Default -Value $ParameterHashtable['IpRangesToWhiteList'].value -Default @()

    # strings
    $graphAppCertificateName        = Get-DefaultString -Value $ParameterHashtable['graphAppCertificateName'].value        -Default 'not-set'
    $teamsChannelAppCertificateName = Get-DefaultString -Value $ParameterHashtable['teamsChannelAppCertificateName'].value -Default 'not-set'
    $tenantDomain                   = Get-DefaultString -Value $ParameterHashtable['tenantDomain'].value                   -Default 'not-set'
    $sharepointDomain               = Get-DefaultString -Value $ParameterHashtable['sharepointDomain'].value               -Default 'not-set'
    $secondaryTenantId              = Get-DefaultString -Value $ParameterHashtable['secondaryTenantId'].value -Default $null

    $hostIpAddress = (Invoke-WebRequest -uri "https://api.ipify.org/").Content
    $ipAddressesToWhiteList = $ipRangesToWhiteList + @($hostIpAddress)
    
    # deploy resource groups
    if ($createResourceGroups -eq $true) {
        Set-ResourceGroups `
            -SubscriptionId $SubscriptionId `
            -Location $Location `
            -ResourceGroupTemplateDirectoryPath $TemplateFilesDirectory `
            -ParameterHashtable $ParameterHashtable `
            -AdditionalParameters $commonParametersObject `
            -SetRBACPermissions $setRBACPermissions
    }

    # deploy prereq resources
    Set-PrereqResources `
        -SolutionAbbreviation           $SolutionAbbreviation `
        -EnvironmentAbbreviation        $EnvironmentAbbreviation `
        -SubscriptionId                 $SubscriptionId `
        -PrereqsTemplateDirectoryPath   $TemplateFilesDirectory `
        -ParameterHashtable             $ParameterHashtable `
        -AdditionalParameters           $commonParametersObject `
        -SetRBACPermissions             $setRBACPermissionsBicep

    Start-Sleep -Seconds 10

    Set-KeyVaultFirewallRules `
        -ResourceGroups @($prereqsResourceGroup) `
        -ipAddresses $ipAddressesToWhiteList `
        -ScriptsDirectory $ScriptsDirectory `
        -Region $Location

    # creating app registrations
    if ($createAppRegistrations -eq $true) {
        Write-Host "`nCreating app registrations"
        $appRegistrations = `
            Set-GMMAppRegistrations `
            -SolutionAbbreviation $SolutionAbbreviation `
            -EnvironmentAbbreviation $EnvironmentAbbreviation `
            -ScriptsDirectory $ScriptsDirectory `
            -SecondaryTenantId $secondaryTenantId `
            -GraphAppCertificateName $graphAppCertificateName `
            -TeamsChannelAppCertificateName $teamsChannelAppCertificateName `
            -TenantDomain $tenantDomain `
            -SharepointDomain $sharepointDomain `
            -SkipAppRegistrationSetupIfAppExists $skipAppRegistrationSetupIfAppExists
    
        Start-Sleep -Seconds 10
    }
   
    # deploy data resources
    Set-DataResources `
        -SolutionAbbreviation       $SolutionAbbreviation `
        -EnvironmentAbbreviation    $EnvironmentAbbreviation `
        -SubscriptionId             $SubscriptionId `
        -DataTemplateDirectoryPath  $TemplateFilesDirectory `
        -ParameterHashtable         $ParameterHashtable `
        -AdditionalParameters       $commonParametersObject `
        -SetRBACPermissions         $setRBACPermissionsBicep 

    Start-Sleep -Seconds 10

    Set-KeyVaultFirewallRules `
        -ResourceGroups @($dataResourceGroup) `
        -ipAddresses $ipAddressesToWhiteList `
        -ScriptsDirectory $ScriptsDirectory `
        -Region $Location
    
    # deploy compute resources
    Set-ComputeResources `
        -SolutionAbbreviation           $SolutionAbbreviation `
        -EnvironmentAbbreviation        $EnvironmentAbbreviation `
        -SubscriptionId                 $SubscriptionId `
        -ComputeTemplateDirectoryPath   $TemplateFilesDirectory `
        -ParameterHashtable             $ParameterHashtable `
        -AdditionalParameters           $commonParametersObject

    Start-Sleep -Seconds 10

    # deploy ADF resources
    if ($skipAzureDataFactoryDeployment -eq $false) {
        Write-Host "`nCreating Azure Data Factory resources"
        Set-ADFResources `
            -SolutionAbbreviation       $SolutionAbbreviation `
            -EnvironmentAbbreviation    $EnvironmentAbbreviation `
            -SubscriptionId             $SubscriptionId `
            -ADFTemplateDirectoryPath   $TemplateFilesDirectory `
            -ParameterHashtable         $ParameterHashtable `
            -AdditionalParameters       $commonParametersObject
        Start-Sleep -Seconds 10
    }
    else {
        Write-Host "`nSkipping Azure Data Factory deployment as per configuration."
    }

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
        [string]$Location
    )

    Write-Host "`nSetting SQL Server firewall rule"
    $dataResourceGroupName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $sqlServerName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $ipAddress = (Invoke-WebRequest -uri "https://api.ipify.org/").Content
    $sqlIPRuleName = "DeploymentScript_Client_IP_Address-$ipAddress"
    $sqlIPRule = Get-AzSqlServerFirewallRule `
        -FirewallRuleName $sqlIPRuleName `
        -ResourceGroupName $dataResourceGroupName `
        -ServerName $sqlServerName `
        -ErrorAction SilentlyContinue

    if ($null -eq $sqlIPRule) {
        Write-Host "Adding firewall rule for SQL Server"
        New-AzSqlServerFirewallRule `
            -ResourceGroupName $dataResourceGroupName `
            -ServerName $sqlServerName `
            -FirewallRuleName $sqlIPRuleName `
            -StartIpAddress $ipAddress `
            -EndIpAddress $ipAddress
    }
}

function Set-SQLServerPermissions {
    param (
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$ConnectionString,
        [Parameter(Mandatory = $true)]
        [string]$ConnectionStringADF
    )

    # SQL Permissions
    Write-Host "`nGranting permissions to SQL database"

    $computeResourceGroup = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation"
    $dataResourceGroup = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"

    # Set the permissions for the user running the script.
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
    
    $roleCommand = $connection.CreateCommand()
    $roleCommand.CommandText = $sqlScript

    Write-Host "Granting permissions to SQL database for $($context.Account.Id)"
    Invoke-SqlOperationWithFirewallRetry `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -SolutionAbbreviation $SolutionAbbreviation `
        -Operation { 
            $connection.Open()
            [void]$roleCommand.ExecuteNonQuery() 
            $connection.Close()
        }

    $roleCommand.Dispose()
    Write-Host "Permissions granted to SQL database for $($context.Account.Id)" -ForegroundColor Green

    # Set the permissions for the function apps.
    $functionApps = Get-AzResource -ResourceGroupName $computeResourceGroup -ResourceType "Microsoft.Web/sites"
    
    foreach ($functionApp in $functionApps) {

        $isWebAPI = $functionApp.Name -match "-webapi"
        $adminRoleClause = "ALTER ROLE db_ddladmin ADD MEMBER [$($functionApp.Name)]"
        
        $functionSqlScript = "IF NOT EXISTS (SELECT * FROM sys.database_principals WHERE name = N'$($functionApp.Name)')
        BEGIN
            CREATE USER [$($functionApp.Name)] FROM EXTERNAL PROVIDER
            ALTER ROLE db_datareader ADD MEMBER [$($functionApp.Name)]
            ALTER ROLE db_datawriter ADD MEMBER [$($functionApp.Name)]
            $($isWebAPI ? $adminRoleClause : '')
        END"

        Write-Host "Granting permissions to SQL database for $($functionApp.Name)"

        $roleCommand = $connection.CreateCommand()
        $roleCommand.CommandText = $functionSqlScript

        Invoke-SqlOperationWithFirewallRetry `
            -EnvironmentAbbreviation $EnvironmentAbbreviation `
            -SolutionAbbreviation $SolutionAbbreviation `
            -Operation { 
                $connection.Open()
                [void]$roleCommand.ExecuteNonQuery() 
                $connection.Close()
            }

        $roleCommand.Dispose()

        Write-Host "Permissions granted to SQL database for $($functionApp.Name)" -ForegroundColor Green
    }

    # ADF Permissions
    $dataFactoryName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation-adf"
    $dataFactory = Get-AzDataFactoryV2 -ResourceGroupName $dataResourceGroup -Name $dataFactoryName -ErrorAction SilentlyContinue
    $functionAppsADF = $functionApps | Where-Object { $_.Name -match "-webapi" -or $_.Name -match "-SqlMembershipObtainer" }

    if ($null -ne $dataFactory) {

        $connectionADF = New-Object System.Data.SqlClient.SqlConnection
        $connectionADF.ConnectionString = $ConnectionStringADF
        $connectionADF.AccessToken = $sqlToken
        
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

        Invoke-SqlOperationWithFirewallRetry `
            -EnvironmentAbbreviation $EnvironmentAbbreviation `
            -SolutionAbbreviation $SolutionAbbreviation `
            -Operation { 
                $connectionADF.Open()
                [void]$roleCommandADF.ExecuteNonQuery()
                $connectionADF.Close()
            }

        $roleCommandADF.Dispose()

        Write-Host "Permissions granted to SQL database for $dataFactoryName" -ForegroundColor Green

        foreach ($functionApp in $functionAppsADF) {

            $functionSqlScript = "IF NOT EXISTS (SELECT * FROM sys.database_principals WHERE name = N'$($functionApp.Name)')
            BEGIN
                CREATE USER [$($functionApp.Name)] FROM EXTERNAL PROVIDER
                ALTER ROLE db_datareader ADD MEMBER [$($functionApp.Name)]
                ALTER ROLE db_datawriter ADD MEMBER [$($functionApp.Name)]
            END"

            Write-Host "Granting permissions to ADF database for $($functionApp.Name)"

            $roleCommandADF = $connectionADF.CreateCommand()
            $roleCommandADF.CommandText = $functionSqlScript

            Invoke-SqlOperationWithFirewallRetry `
                -EnvironmentAbbreviation $EnvironmentAbbreviation `
                -SolutionAbbreviation $SolutionAbbreviation `
                -Operation { 
                    $connectionADF.Open()
                    [void]$roleCommandADF.ExecuteNonQuery()
                    $connectionADF.Close()
                }

            $roleCommandADF.Dispose()

            Write-Host "Permissions granted to ADF database for $($functionApp.Name)" -ForegroundColor Green
        }
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

    . ($ScriptsDirectory + '/Set-PostDeploymentRoles.ps1')
    Set-PostDeploymentRoles `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -SetUserAssignedManagedIdentityPermissions $SetUserAssignedManagedIdentityPermissions

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
        $packageFile = "$FunctionsPackagesDirectory/$functionName.zip"

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
        Publish-AzWebApp -ResourceGroupName $ComputeResourceGroup -Name $webApi.Name -ArchivePath "$WebApiPackagesDirectory/$webApiName.zip" -Force
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
        [string[]]$ipAddresses,
        [Parameter(Mandatory = $true)]
        [string]$ScriptsDirectory,
        [Parameter(Mandatory = $true)]
        [string]$Region
    )

    Write-Host "Enabling firewall rules for key vaults"

    # Get IP rules from script
    . ($ScriptsDirectory + '/Get-FirewallIPRules.ps1') -FolderPathToSaveIpRules $ScriptsDirectory -Regions $Region
    $newIpRules = Get-Content "$ScriptsDirectory/ipRules.txt"
    $newIpRules += $ipAddresses

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
            $latestSecretVersion = Get-KeyVaultSecretWithFirewallRetry -ResourceGroup $kvReference.KeyVaultName -VaultName $kvReference.KeyVaultName -SecretName $kvReference.SecretName

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
            $latestSecretVersion = Get-KeyVaultSecretWithFirewallRetry -ResourceGroup $kvReference.KeyVaultName -VaultName $kvReference.KeyVaultName -SecretName $kvReference.SecretName

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
    . ($ScriptsDirectory + '/ApplicationSetupScripts/Set-UIAzureADApplication.ps1')

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

    . ($ScriptsDirectory + '/ApplicationSetupScripts/Set-WebApiAzureADApplication.ps1')
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

    . ($ScriptsDirectory + '/ApplicationSetupScripts/Set-GraphCredentialsAzureADApplication.ps1')
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

    . ($ScriptsDirectory + '/ApplicationSetupScripts/Set-TeamsChannelAzureADApplication.ps1')
    $teamsChannelInformation = Set-TeamsChannelAzureADApplication `
        -SubscriptionName $subscriptionName `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -TenantIdToCreateAppIn (Get-Default -Value $SecondaryTenantId -Default $mainTenantId) `
        -TenantIdWithKeyVault $mainTenantId `
        -SaveToKeyVault $true `
        -SkipPrompts $true `
        -SkipIfApplicationExists $SkipAppRegistrationSetupIfAppExists `
        -CertificateName $TeamsChannelAppCertificateName `
        -Clean $false

    $null = Set-AzContext -Tenant $mainTenantId -Subscription $subscriptionName

    # determine which apps need admin consent
    $appInformationObjects = @(
        $uiInformation,
        $apiInformation,
        $graphInformation,
        $teamsChannelInformation
    )

    $appsThatNeedAdminConsent = @()

    foreach ($appInfo in $appInformationObjects) {
        if ($appInfo.UpdatedApiPermissions) {
            $appsThatNeedAdminConsent += @{
                ApplicationId   = $appInfo.ApplicationId;
                ApplicationName = $appInfo.ApplicationName
            }
        }
    }

    #return the response
    return @{
        UIApplicationId             = $uiInformation.ApplicationId;
        UITenantId                  = $uiInformation.TenantId;
        APIApplicationId            = $apiInformation.ApplicationId;
        APITenantId                 = $apiInformation.TenantId;
        GraphApplicationId          = $graphInformation.ApplicationId;
        GraphTenantId               = $graphInformation.TenantId;
        TeamsChannelApplicationId   = $teamsChannelInformation.ApplicationId;
        TeamsChannelTenantId        = $teamsChannelInformation.TenantId;
        AppsThatNeedAdminConsent    = $appsThatNeedAdminConsent;
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
        [System.Nullable[Guid]]$DevTenantId,
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
        [AllowNull()]
        [AllowEmptyString()]
        [string]$UIClientId,
        [Parameter(Mandatory = $false)]
        [AllowNull()]
        [AllowEmptyString()]
        [string]$UITenantId,
        [Parameter(Mandatory = $false)]
        [AllowNull()]
        [AllowEmptyString()]
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
        $UIClientId = Get-KeyVaultSecretWithFirewallRetry `
                        -ResourceGroup "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation" `
                        -VaultName "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation" `
                        -SecretName "uiAppId" `
                        -AsPlainText
    } 

    if ([string]::IsNullOrWhiteSpace($UITenantId)) {
        $UITenantId = Get-KeyVaultSecretWithFirewallRetry `
                        -ResourceGroup "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation" `
                        -VaultName "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation" `
                        -SecretName "uiTenantId" `
                        -AsPlainText
    } 

    if ([string]::IsNullOrWhiteSpace($WebApiClientId)) {
        $WebApiClientId = Get-KeyVaultSecretWithFirewallRetry `
                            -ResourceGroup "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation" `
                            -VaultName "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation" `
                            -SecretName "webApiClientId" `
                            -AsPlainText
    } 
    
    $appInsights = Get-AzApplicationInsights -ResourceGroupName $DataResourceGroup  -Name "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $appInsightsConnectionString = $appInsights.ConnectionString

    $buildVersionFilePath = "$WebAppDirectory/buildVersion.txt"
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

    Set-Content -Path "$WebAppDirectory/.env" -Value $envContent -Force
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
    $scriptsDirectory = Split-Path $PSScriptRoot -Parent

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
    $requiredPaths = @{
        "function_packages"        = "$scriptsDirectory/function_packages"
        "webapi_package"          = "$scriptsDirectory/webapi_package"
        "webapp_package/web-app"  = "$scriptsDirectory/webapp_package/web-app"
        "Scripts"                 = "$scriptsDirectory/Scripts"
        "Scripts/PostDeploymentRoleAssignments"  = "$scriptsDirectory/Scripts/PostDeploymentRoleAssignments"
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

function Install-RequiredModules {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$ScriptsDirectory
    )

    # Install Az modules
    Write-Host "Installing/Importing required Az modules..."
    . ($ScriptsDirectory + '/Install-AzModuleIfNeeded.ps1')
    Install-AzModuleIfNeeded | Out-Null
    Write-Host "Completed installation/import of required Az modules." -ForegroundColor Green

    # Install Microsoft Graph modules
    Write-Host "Installing/Importing required Microsoft Graph PowerShell modules..."
    . ($ScriptsDirectory + '/Install-ModuleIfNeeded.ps1')
		
    $requiredGraphModules = @(
        "Microsoft.Graph.Authentication",
        "Microsoft.Graph.Applications",
        "Microsoft.Graph.Identity.DirectoryManagement",
        "Microsoft.Graph.Users"
    )

    . ($ScriptsDirectory + '/Install-ModuleIfNeeded.ps1')

    foreach ($module in $requiredGraphModules) {
        Install-ModuleIfNeeded -Name $module -Version "2.17.0" -Verbose
    }
    Write-Host "Completed installation/import of required Microsoft Graph PowerShell modules." -ForegroundColor Green

    # Install MSIdentityTools module to get Azure IP ranges
    Write-Host "Installing MSIdentityTools..."
    Install-ModuleIfNeeded -Name MSIdentityTools -Version "2.0.52" -Verbose
    Write-Host "Completed installation/import of MSIdentityTools Module." -ForegroundColor Green
}

function Initialize-ScriptDependencies {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$Location,
        [Parameter(Mandatory = $true)]
        [string]$SubscriptionId,
        [Parameter(Mandatory = $true)]
        [string]$ScriptsDirectory,
        [Parameter(Mandatory = $true)]
        [bool]$UseDeviceAuthentication,
        [Parameter(Mandatory = $true)]
        [bool]$SkipModuleInstallation,
        [Parameter(Mandatory = $false)]
        [bool]$AssertUserPermissions = $true
    )

    Test-ScriptDependencies

    if ($SkipModuleInstallation -eq $true) {
        Write-Host "Skipping module installation as per configuration." -ForegroundColor Yellow
    } else {
        Write-Host "Installing required PowerShell modules..."
        Install-RequiredModules -ScriptsDirectory $ScriptsDirectory
    }

    # Connect to Microsoft Graph with required scopes
    $requiredScopes = @(
        "AppRoleAssignment.ReadWrite.All",
        "Directory.ReadWrite.All"
    )

    Write-Host "Disconnecting any existing Microsoft Graph sessions..."
    Disconnect-MgGraph -ErrorAction SilentlyContinue | Out-Null

    # Connect to Microsoft Graph and Azure
    if ($UseDeviceAuthentication -eq $true) {
        Write-Host "Connecting to Microsoft Graph using device code authentication..."
        Connect-MgGraph -Scopes $requiredScopes -NoWelcome -UseDeviceCode

        Write-Host "Connecting to Azure using device code authentication..."
        Connect-AzAccount -UseDeviceAuthentication
    }
    else {
        Write-Host "Connecting to Microsoft Graph using interactive authentication..."
        Connect-MgGraph -Scopes $requiredScopes -NoWelcome
        Write-Host "Connecting to Azure using interactive authentication..."
        Connect-AzAccount
    }

    Set-Subscription `
            -ScriptsDirectory $ScriptsDirectory `
            -SubscriptionId $SubscriptionId

    if ($AssertUserPermissions -eq $true) {
        . ($ScriptsDirectory + '/Assert-MicrosoftGraphPermissions.ps1')
        Assert-MicrosoftGraphPermissions

        . ($ScriptsDirectory + '/Assert-RbacPermissionsForDeployment.ps1')
        Assert-RbacPermissionsForDeployment `
            -SolutionAbbreviation $SolutionAbbreviation `
            -EnvironmentAbbreviation $EnvironmentAbbreviation
    }
}

function Assert-RequiredParameters {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [hashtable]$ParameterHashtable
    )

    Write-Host "Verifying required parameters are provided..."

    foreach ($key in $ParameterHashtable.Keys) {
        $entry = $ParameterHashtable[$key]
        $value = $entry["value"]
        $metadata = $entry["metadata"]

        if ($metadata -and $metadata["required"] -eq $true) {
            $isEmpty = $false

            if ($value -eq $null) {
                $isEmpty = $true
            } elseif ($value -is [string] -and [string]::IsNullOrWhiteSpace($value)) {
                $isEmpty = $true
            } elseif ($value -is [System.Collections.IEnumerable] -and $value.Count -eq 0) {
                $isEmpty = $true
            }

            if ($isEmpty) {
                throw "Required parameter '$key' is missing or empty. Please provide all required parameters in the parameters file."
            }
        }
    }

    if ($ParameterHashtable["appConfigurationDataOwners"].value[0] -eq "<guid>") {
        throw "Required parameter appConfigurationDataOwners is missing or empty. Please replace `<guid>` with a valid GUID."
    }

    Write-Host "All required parameters are provided." -ForegroundColor Green
}

function Start-EFMigrationViaWebAPI {
    param (
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation
    )

    # Call the WebAPI to perform EF migrations. The WebAPI performs EF migrations on startup. Calling any endpoint will trigger the startup process if the app is not already running.
    # The endpoint will always return an error code, so we catch the error and ignore it.
    Try{
        Write-Host "`nInvoking WebAPI to perform EF migrations..."
        Invoke-WebRequest -Uri "https://$SolutionAbbreviation-compute-$EnvironmentAbbreviation-webapi.azurewebsites.net/" | Out-Null
    }
    Catch{
        # Ignore the error
    }
    Finally {
       Write-Host "WebAPI invocation completed." -ForegroundColor Green
    }
}


function Deploy-Resources {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $false)]
        [string]$ParameterFileName = "parameters.json"
    )

    $global:SkipModuleInstall = $true
    $global:SkipMSGraphLogin = $true
    $global:SkipAzLogin = $true

    $deploymentPackageDirectory = Split-Path $PSScriptRoot -Parent
    $templateFilesDirectory = $deploymentPackageDirectory + "/Deployment"
    $scriptsDirectory = $deploymentPackageDirectory + "/Scripts"

    $parameterFilePath = $deploymentPackageDirectory + "/Deployment/$ParameterFileName"
    $parameterHashtable= (Get-TemplateAsHashtable -TemplateFilePath $parameterFilePath).parameters

    Assert-RequiredParameters -ParameterHashtable $parameterHashtable

    $solutionAbbreviation                           = $parameterHashtable.solutionAbbreviation.value
    $environmentAbbreviation                        = $parameterHashtable.environmentAbbreviation.value
    $location                                       = $parameterHashtable.location.value
    $subscriptionId                                 = $parameterHashtable.subscriptionId.value
    $skipResourceProvidersCheck                     = $parameterHashtable.skipResourceProvidersCheck.value
    $startFunctions                                 = $parameterHashtable.startFunctions.value
    $assertUserPermissions                          = $parameterHashtable.assertUserPermissions.value
    $setUserAssignedManagedIdentityPermissions      = $parameterHashtable.setUserAssignedManagedIdentityPermissions.value
    $isInitialDeployment                            = $parameterHashtable.isInitialDeployment.value
    $resetGMMType                                   = $parameterHashtable.resetGMMType.value
    $useDeviceAuthentication                        = $parameterHashtable.useDeviceAuthentication.value
    $skipModuleInstallation                         = $parameterHashtable.skipModuleInstallation.value

    $setRBACPermissions             = Get-Default -Value $ParameterHashtable['setRBACPermissions'].value      -Default $false
    $createAppRegistrations         = Get-Default -Value $ParameterHashtable['createAppRegistrations'].value  -Default $true
    $skipSqlServerPermissionSetup   = Get-Default -Value $ParameterHashtable['skipSqlServerPermissionSetup'].value -Default $false
    $tenantDomain                   = Get-DefaultString -Value $ParameterHashtable['tenantDomain'].value                   -Default 'not-set'
    $sharepointDomain               = Get-DefaultString -Value $ParameterHashtable['sharepointDomain'].value               -Default 'not-set'
    $secondaryTenantId              = Get-DefaultString -Value $ParameterHashtable['secondaryTenantId'].value -Default $null

    $computeResourceGroup = "$SolutionAbbreviation-compute-$environmentAbbreviation"
    $dataResourceGroup = "$SolutionAbbreviation-data-$environmentAbbreviation"

    Initialize-ScriptDependencies `
        -SolutionAbbreviation $solutionAbbreviation `
        -EnvironmentAbbreviation $environmentAbbreviation `
        -Location $location `
        -SubscriptionId $subscriptionId `
        -ScriptsDirectory $scriptsDirectory `
        -UseDeviceAuthentication $useDeviceAuthentication `
        -SkipModuleInstallation $skipModuleInstallation `
        -AssertUserPermissions $assertUserPermissions

    if (!$skipResourceProvidersCheck) {
        Set-ResourceProviders
    }

    if(!$isInitialDeployment) {
        
        $jobTrigger = Get-AzFunctionApp -ResourceGroupName $computeResourceGroup `
                                        -Name "$computeResourceGroup-JobTrigger"

        Write-Host "`nStopping JobTrigger function app to prevent interference with deployment..."
        Stop-AzFunctionApp -ResourceGroupName $computeResourceGroup -Name $jobTrigger.Name -Force
        Write-Host "JobTrigger function app stopped." -ForegroundColor Green

        . "$scriptsDirectory/Reset-GMM.ps1" #  Import helper functions

        $connectionString = Get-KeyVaultSecretWithFirewallRetry `
            -VaultName "$SolutionAbbreviation-data-$environmentAbbreviation" `
            -ResourceGroup $dataResourceGroup `
            -SecretName "sqlDatabaseConnectionString"

        $connectionStringADF = Get-KeyVaultSecretWithFirewallRetry `
            -VaultName "$SolutionAbbreviation-data-$environmentAbbreviation" `
            -ResourceGroup $dataResourceGroup `
            -SecretName "sqlServerBasicConnectionString"

        Set-PreDeploymentUpdates `
            -ScriptsDirectory $scriptsDirectory `
            -SolutionAbbreviation $solutionAbbreviation `
            -EnvironmentAbbreviation $environmentAbbreviation `
            -SyncJobsDBConnectionString $connectionString `
            -ADFDBConnectionString $connectionStringADF
    }


    # Import reusable functions
    . ($scriptsDirectory + '/ReusableModules/Get-KeyVaultSecretWithFirewallRetry.ps1')
    . ($scriptsDirectory + '/ReusableModules/Set-KeyVaultSecretWithFirewallRetry.ps1')
    . ($scriptsDirectory + '/ReusableModules/Invoke-SqlOperationWithFirewallRetry.ps1')

    $response = Set-GMMResources `
        -SolutionAbbreviation $solutionAbbreviation `
        -EnvironmentAbbreviation $environmentAbbreviation `
        -SubscriptionId $subscriptionId `
        -Location $location `
        -TemplateFilesDirectory $TemplateFilesDirectory `
        -ScriptsDirectory $ScriptsDirectory `
        -ParameterHashtable $parameterHashtable

    Start-Sleep -Seconds 30

    Update-AppSettingsVersion -ComputeResourceGroupName $computeResourceGroup

    Set-SqlServerFirewallRule `
        -SolutionAbbreviation $solutionAbbreviation `
        -EnvironmentAbbreviation $environmentAbbreviation `
        -Location $location

    # retrieve SQL connection strings
    # Basic connection string
    $connectionString = Get-KeyVaultSecretWithFirewallRetry `
            -VaultName "$SolutionAbbreviation-data-$EnvironmentAbbreviation" `
            -ResourceGroup $dataResourceGroup `
            -SecretName "sqlDatabaseConnectionString" `
            -AsPlainText

    $connectionStringADF = Get-KeyVaultSecretWithFirewallRetry `
        -VaultName "$SolutionAbbreviation-data-$EnvironmentAbbreviation" `
        -ResourceGroup $dataResourceGroup `
        -SecretName "sqlServerBasicConnectionString" `
        -AsPlainText

    if ($false -eq $skipSqlServerPermissionSetup) {
        Set-SQLServerPermissions `
            -SolutionAbbreviation $solutionAbbreviation `
            -EnvironmentAbbreviation $environmentAbbreviation `
            -ConnectionString $connectionString `
            -ConnectionStringADF $connectionStringADF
    }

    if ($true -eq $setRBACPermissions) {
        Set-RBACPermissions `
        -SolutionAbbreviation $solutionAbbreviation `
        -EnvironmentAbbreviation $environmentAbbreviation `
        -ScriptsDirectory "$scriptsDirectory/PostDeploymentRoleAssignments" `
        -SetUserAssignedManagedIdentityPermissions $setUserAssignedManagedIdentityPermissions
    }

    Set-FunctionAppCode `
        -ComputeResourceGroup $computeResourceGroup `
        -FunctionsPackagesDirectory "$deploymentPackageDirectory/function_packages" `
        -WebApiPackagesDirectory "$deploymentPackageDirectory/webapi_package"

    # Configure web apps
    if ($true -eq $createAppRegistrations) {
        Set-ConfigureWebApps `
            -WebApiName "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-webapi" `
            -UIWebAppName "$SolutionAbbreviation-ui" `
            -DevTenantId $secondaryTenantId `
            -UIAppRegistrationId $response.AppRegistrations.UIApplicationId `
            -ComputeResourceGroup $computeResourceGroup
    }

    $context = Get-AzContext

    # Publish UI code
    Set-PublishUICode `
        -UIClientId $response.AppRegistrations.UIApplicationId `
        -UITenantId $response.AppRegistrations.UITenantId `
        -WebApiClientId $response.AppRegistrations.APIApplicationId `
        -WebApiBaseUri "https://$SolutionAbbreviation-compute-$EnvironmentAbbreviation-webapi.azurewebsites.net" `
        -SolutionAbbreviation $solutionAbbreviation `
        -EnvironmentAbbreviation $environmentAbbreviation `
        -DataResourceGroup $dataResourceGroup `
        -ComputeResourceGroup $computeResourceGroup `
        -WebAppDirectory "$deploymentPackageDirectory/webapp_package/web-app" `
        -MainTenantId $context.Tenant.Id `
        -TenantDomain $tenantDomain `
        -SharepointDomain $sharepointDomain `
        -SubscriptionId $subscriptionId
    
    # Call the WebAPI to perform EF migrations.
    Start-EFMigrationViaWebAPI `
        -SolutionAbbreviation $solutionAbbreviation `
        -EnvironmentAbbreviation $environmentAbbreviation

    Set-PostDeploymentUpdates `
        -EnvironmentAbbreviation $environmentAbbreviation `
        -SolutionAbbreviation $solutionAbbreviation `
        -ScriptsDirectory $scriptsDirectory `
        -ConnectionString $connectionString

    if(!$isInitialDeployment -and $resetGMMType -ne "Skip") {
        Write-Host "`nStopping function apps in resource group $computeResourceGroup"
        Stop-FunctionApps -ResourceGroupName $computeResourceGroup

        . ($scriptsDirectory + '/Reset-GMM.ps1')

        Set-WebAPIAsResetAdministrator `
            -SolutionAbbreviation $solutionAbbreviation `
            -EnvironmentAbbreviation $environmentAbbreviation

        if($resetGMMType -eq "Credentials") {
            Reset-GMMWithCredentials `
                -SolutionAbbreviation $solutionAbbreviation `
                -EnvironmentAbbreviation $environmentAbbreviation
        } elseif ($resetGMMType -eq "ServicePrincipal") {
            Reset-GMMWithServicePrincipal `
                -SolutionAbbreviation $solutionAbbreviation `
                -EnvironmentAbbreviation $environmentAbbreviation
        }
    }

    if ($startFunctions) {
        Start-FunctionApps -ResourceGroupName $computeResourceGroup
    }

    Write-Host "`nDeployment complete!" -ForegroundColor Green

    if ($response.AppRegistrations.AppsThatNeedAdminConsent.Count -gt 0) {
        Write-Host "`n==========================================================" -ForegroundColor Yellow
        Write-Host "The following applications might require admin consent:" -ForegroundColor Yellow
        Write-Host "==========================================================" -ForegroundColor Yellow

        foreach ($app in $response.AppRegistrations.AppsThatNeedAdminConsent) {
            $consentUrl = "https://portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationMenuBlade/~/CallAnAPI/appId/$($app.ApplicationId)"
            Write-Host ("`n{0} - {1}" -f $app.ApplicationName, $consentUrl) -ForegroundColor Cyan
        }

        Write-Host "`nPlease use the provided URLs to open the Azure portal and grant admin consent for these applications." -ForegroundColor Yellow
    }

    Write-Host "`n`n==========================================================" -ForegroundColor Yellow
    Write-Host "Access the GMM UI here:" -ForegroundColor Yellow
    Write-Host "==========================================================" -ForegroundColor Yellow


    $staticWebApp = Get-AzStaticWebApp -Name "$SolutionAbbreviation-ui" -ResourceGroupName $computeResourceGroup
    if ($null -ne $staticWebApp) {
        Write-Host "`nhttps://$($staticWebApp.DefaultHostname)`n" -ForegroundColor Cyan
    }
}