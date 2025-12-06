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
        [string]$ADFDBConnectionString,
        [Parameter(Mandatory = $false)]
        [bool]$SetRBACPermissions = $false
    )

    . ($ScriptsDirectory + '/PreDeploymentMigrations/Set-PreDeploymentMigrations.ps1')

    Set-PreDeploymentMigrations `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -SyncJobsDBConnectionString $SyncJobsDBConnectionString `
        -ADFDBConnectionString $ADFDBConnectionString `
        -SetRBACPermissions $SetRBACPermissions
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
    $setRBACPermissionsBicep = Get-Default -Value $ParameterHashtable['setRBACPermissionsBicep'].value -Default $false
    $createResourceGroups = Get-Default -Value $ParameterHashtable['createResourceGroups'].value -Default $false
    $skipAzureDataFactoryDeployment = Get-Default -Value $ParameterHashtable['skipAzureDataFactoryDeployment'].value -Default $false
    $ipRangesToWhiteList = Get-Default -Value $ParameterHashtable['IpRangesToWhiteList'].value -Default @()

    # strings
    $graphAppCertificateName        = Get-DefaultString -Value $ParameterHashtable['graphAppCertificateName'].value        -Default 'not-set'
    $teamsChannelAppCertificateName = Get-DefaultString -Value $ParameterHashtable['teamsChannelAppCertificateName'].value -Default 'not-set'
    $tenantDomain                   = Get-DefaultString -Value $ParameterHashtable['tenantDomain'].value                   -Default 'not-set'
    $sharepointDomain               = Get-DefaultString -Value $ParameterHashtable['sharepointDomain'].value               -Default 'not-set'
    $directoryTenantId              = Get-DefaultString -Value $ParameterHashtable['directoryTenantId'].value -Default $ParameterHashtable.tenantId.value

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

    # Store the app registration secrets
    if ($ParameterHashtable.skipAppRegistrationSecretStorage.value -ne $true) {
        $isClientSecretAuth = if ($ParameterHashtable.authenticationType.value -eq "ClientSecret") { $true } else { $false }
        Save-GMMAppRegistrationSecrets `
            -SolutionAbbreviation $SolutionAbbreviation `
            -EnvironmentAbbreviation $EnvironmentAbbreviation `
            -ScriptsDirectory $ScriptsDirectory `
            -AppTenantId $directoryTenantId `
            -GraphAppCertificateName $graphAppCertificateName `
            -TeamsChannelAppCertificateName $teamsChannelAppCertificateName `
            -TenantDomain $tenantDomain `
            -SharepointDomain $sharepointDomain `
            -SkipPrivilegedDirectoryActions $ParameterHashtable.skipPrivilegedDirectoryActions.value `
            -IsClientSecretAuth $isClientSecretAuth
            
        Start-Sleep -Seconds 10
    }
    else {
        Write-Host "`nSkipping app registration secret storage as per configuration [skipAppRegistrationSecretStorage = $($ParameterHashtable.skipAppRegistrationSecretStorage.value)]." -ForegroundColor Yellow
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
        Write-Host "`nSkipping Azure Data Factory deployment as per configuration [skipAzureDataFactoryDeployment = $skipAzureDataFactoryDeployment]."
    }

    Write-Host "`nResources deployed"
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
        [string]$TenantId,
        [Parameter(Mandatory = $true)]
        [string]$ScriptsDirectory,
        [Parameter(Mandatory = $false)]
        [bool] $SetUserAssignedManagedIdentityPermissions = $false,
        [Parameter(Mandatory = $false)]
        [boolean] $SkipPrivilegedDirectoryActions = $false
    )

    # grant permissions to resources
    Write-Host "`nGranting permissions to resources"

    . ($ScriptsDirectory + '/Set-PostDeploymentRoles.ps1')
    Set-PostDeploymentRoles `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -TenantId $TenantId `
        -SetUserAssignedManagedIdentityPermissions $SetUserAssignedManagedIdentityPermissions `
        -SkipPrivilegedDirectoryActions $SkipPrivilegedDirectoryActions

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

function Set-GMMAppRegistrationsProgrammatically {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$ScriptsDirectory,
        [Parameter(Mandatory = $false)]
        [string]$DirectoryTenantId
    )

    Write-Host "`n📝 Creating app registrations programmatically...`n" -ForegroundColor Cyan

    . ($ScriptsDirectory + '/ApplicationSetupScripts/Set-UIAzureADApplication.ps1')
    $uiInformation = Set-UIAzureADApplication `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -AppTenantId $DirectoryTenantId `
        -SaveToKeyVault $false `
        -SkipIfApplicationExists $false `
        -Clean $false

    . ($ScriptsDirectory + '/ApplicationSetupScripts/Set-WebApiAzureADApplication.ps1')
    $apiInformation = Set-WebApiAzureADApplication `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -AppTenantId $DirectoryTenantId `
        -SaveToKeyVault $false `
        -SkipIfApplicationExists $false `
        -Clean $false

    . ($ScriptsDirectory + '/ApplicationSetupScripts/Set-GraphCredentialsAzureADApplication.ps1')
    $graphInformation = Set-GraphCredentialsAzureADApplication `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -AppTenantId $DirectoryTenantId `
        -SaveToKeyVault $false `
        -SkipIfApplicationExists $false `
        -Clean $false

    . ($ScriptsDirectory + '/ApplicationSetupScripts/Set-TeamsChannelAzureADApplication.ps1')
    $teamsChannelInformation = Set-TeamsChannelAzureADApplication `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -AppTenantId $DirectoryTenantId `
        -SaveToKeyVault $false `
        -SkipIfApplicationExists $false `
        -Clean $false


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
        UIAppId = $uiInformation.ApplicationId
        WebApiAppId = $apiInformation.ApplicationId
        GraphAppId = $graphInformation.ApplicationId
        TeamsChannelAppId = $teamsChannelInformation.ApplicationId
        AppsThatNeedAdminConsent = $appsThatNeedAdminConsent
    }
}

function Save-GMMAppRegistrationSecrets {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$ScriptsDirectory,
        [Parameter(Mandatory = $true)]
        [string]$AppTenantId,
        [Parameter(Mandatory = $true)]
        [boolean]$IsClientSecretAuth,
        [Parameter(Mandatory = $false)]
        [boolean]$SkipPrivilegedDirectoryActions,
        [Parameter(Mandatory = $False)]
        [string] $GraphAppCertificateName,
        [Parameter(Mandatory = $False)]
        [string] $TeamsChannelAppCertificateName,
        [Parameter(Mandatory = $false)]
        [string]$TenantDomain,
        [Parameter(Mandatory = $false)]
        [string]$SharepointDomain
    )

    Write-Host "`n🔐 Saving App Registration Secrets to Key Vault" -ForegroundColor Cyan
    Write-Host "═══════════════════════════════════════════════════════════════════════════" -ForegroundColor Cyan

    $applicationSetupScriptsDirectory = Join-Path $ScriptsDirectory "ApplicationSetupScripts"

    # Retrieve Application IDs
    Write-Host "`n📋 Retrieving App Registration IDs..." -ForegroundColor Yellow
    
    $uiAppId = (Get-MgApplication -Filter "displayName eq '$SolutionAbbreviation-ui-$EnvironmentAbbreviation'").AppId
    if (-not $uiAppId) {
        Write-Error "UI Application '$SolutionAbbreviation-ui-$EnvironmentAbbreviation' not found"
        return
    }
    Write-Host "  ✓ UI App ID: $uiAppId" -ForegroundColor Gray

    $webApiAppId = (Get-MgApplication -Filter "displayName eq '$SolutionAbbreviation-webapi-$EnvironmentAbbreviation'").AppId
    if (-not $webApiAppId) {
        Write-Error "WebAPI Application '$SolutionAbbreviation-webapi-$EnvironmentAbbreviation' not found"
        return
    }
    Write-Host "  ✓ WebAPI App ID: $webApiAppId" -ForegroundColor Gray

    $graphAppId = (Get-MgApplication -Filter "displayName eq '$SolutionAbbreviation-Graph-$EnvironmentAbbreviation'").AppId
    if (-not $graphAppId) {
        Write-Error "Graph Application '$SolutionAbbreviation-Graph-$EnvironmentAbbreviation' not found"
        return
    }
    Write-Host "  ✓ Graph App ID: $graphAppId" -ForegroundColor Gray

    $teamsChannelAppId = (Get-MgApplication -Filter "displayName eq '$SolutionAbbreviation-TeamsChannel-$EnvironmentAbbreviation'").AppId
    if (-not $teamsChannelAppId) {
        Write-Error "Teams Channel Application '$SolutionAbbreviation-TeamsChannel-$EnvironmentAbbreviation' not found"
        return
    }
    Write-Host "  ✓ Teams Channel App ID: $teamsChannelAppId" -ForegroundColor Gray

    $createNewSecrets = $false
    $askForSecretInput = $false
    if ($IsClientSecretAuth -eq $true) {
       if ($SkipPrivilegedDirectoryActions -eq $true) {
            # Ask user if they want to input secrets now
            Write-Host "`n🔐 Application Secret Configuration" -ForegroundColor Cyan
            Write-Host "═══════════════════════════════════════════════════════════════════════════" -ForegroundColor Cyan
            Write-Host ""
            Write-Host "You are using Client Secret authentication and the 'SkipPrivilegedDirectoryActions' flag is enabled. The deployment script needs the" -ForegroundColor White
            Write-Host "client secrets for each application to store them securely in Key Vault." -ForegroundColor White
            Write-Host ""
            Write-Host "Would you like to input the application secrets now?" -ForegroundColor Yellow
            Write-Host "  (Choose 'No' if you have already saved the secrets in a previous deployment)" -ForegroundColor Gray
            Write-Host ""
            
            $response = Read-Host "Input application secrets now? (Y/N)"
            
            if ($response -notmatch '^[Yy]') {
                Write-Host "`n⏭️  Skipping application secret input." -ForegroundColor Yellow
                Write-Host "   If you need to update secrets later, you can run this deployment again" -ForegroundColor Gray
                Write-Host "   or manually update them in the Key Vault.`n" -ForegroundColor Gray
                return
            }
            
            $askForSecretInput = $true
        }
        else {
            $createNewSecrets = $true
        }
    }

    # If user needs to provide secrets manually
    $manualSecrets = @{}
    if ($askForSecretInput -eq $true) {
        Write-Host "`n⚠️  MANUAL SECRET INPUT REQUIRED" -ForegroundColor Yellow
        Write-Host "═══════════════════════════════════════════════════════════════════════════" -ForegroundColor Yellow
        Write-Host "`nPlease provide the client secrets that you created manually for each app registration.`n" -ForegroundColor White
        
        Write-Host "📋 Please enter the client secrets for the following applications:" -ForegroundColor Cyan
        Write-Host "   (These secrets will be securely stored in Key Vault)`n" -ForegroundColor Gray
        
        Write-Host "1️⃣  UI Application ($SolutionAbbreviation-ui-$EnvironmentAbbreviation)" -ForegroundColor Green
        do {
            $appSecret = Read-Host "   Enter UI App Client Secret"
             
            if ([string]::IsNullOrWhiteSpace($appSecret)) {
                Write-Host "   ❌ Secret cannot be empty or contain only whitespace. Please try again." -ForegroundColor Red
            }
        } while ([string]::IsNullOrWhiteSpace($appSecret))

        $manualSecrets['UISecret'] = $appSecret

        Write-Host "`n2️⃣  WebAPI Application ($SolutionAbbreviation-webapi-$EnvironmentAbbreviation)" -ForegroundColor Green
        do {
            $appSecret = Read-Host "   Enter WebAPI App Client Secret"
            if ([string]::IsNullOrWhiteSpace($appSecret)) {
                Write-Host "   ❌ Secret cannot be empty or contain only whitespace. Please try again." -ForegroundColor Red
            }
        } while ([string]::IsNullOrWhiteSpace($appSecret))

        $manualSecrets['WebApiSecret'] = $appSecret

        Write-Host "`n3️⃣  Graph Application ($SolutionAbbreviation-Graph-$EnvironmentAbbreviation)" -ForegroundColor Green
        do {
            $appSecret = Read-Host "   Enter Graph App Client Secret"
            if ([string]::IsNullOrWhiteSpace($appSecret)) {
                Write-Host "   ❌ Secret cannot be empty or contain only whitespace. Please try again." -ForegroundColor Red
            }
        } while ([string]::IsNullOrWhiteSpace($appSecret))

        $manualSecrets['GraphSecret'] = $appSecret

        Write-Host "`n4️⃣  Teams Channel Application ($SolutionAbbreviation-TeamsChannel-$EnvironmentAbbreviation)" -ForegroundColor Green
        do {
            $appSecret = Read-Host "   Enter Teams Channel App Client Secret" 

            if ([string]::IsNullOrWhiteSpace($appSecret)) {
                Write-Host "   ❌ Secret cannot be empty or contain only whitespace. Please try again." -ForegroundColor Red
            }
        } while ([string]::IsNullOrWhiteSpace($appSecret))

        $manualSecrets['TeamsChannelSecret'] = $appSecret

        Write-Host "`n✅ All secrets collected. Proceeding to save them to Key Vault...`n" -ForegroundColor Green
        Write-Host "═══════════════════════════════════════════════════════════════════════════`n" -ForegroundColor Yellow
    }

    # UI Application Secrets
    Write-Host "`n📝 Saving UI Application secrets..." -ForegroundColor Yellow
    $uiScriptPath = Join-Path $applicationSetupScriptsDirectory "Set-UIAzureADApplication.ps1"
    . $uiScriptPath

    $uiSecret = if ($askForSecretInput -eq $true) {$manualSecrets['UISecret']} else {"not-set"}
    Set-UIKeyVaultSecrets `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -AppTenantId $AppTenantId `
        -UIApplicationId $uiAppId `
        -CreateNewSecret $createNewSecrets `
        -TenantDomain $TenantDomain `
        -SharepointDomain $SharepointDomain `
        -AppSecret $uiSecret

    Write-Host "✅ UI Application secrets saved" -ForegroundColor Green

    # WebAPI Application Secrets
    Write-Host "`n📝 Saving WebAPI Application secrets..." -ForegroundColor Yellow
    $webApiScriptPath = Join-Path $applicationSetupScriptsDirectory "Set-WebApiAzureADApplication.ps1"
    . $webApiScriptPath

    $webApiSecret = if ($askForSecretInput -eq $true) {$manualSecrets['WebApiSecret']} else {"not-set"}
    Set-WebAPIKeyVaultSecrets -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -AppTenantId $AppTenantId `
        -WebApiApplicationId $webApiAppId `
        -CreateNewSecret $createNewSecrets `
        -AppSecret $webApiSecret

    Write-Host "✅ WebAPI Application secrets saved" -ForegroundColor Green

    # Graph Application Secrets
    Write-Host "`n📝 Saving Graph Application secrets..." -ForegroundColor Yellow
    $graphScriptPath = Join-Path $applicationSetupScriptsDirectory "Set-GraphCredentialsAzureADApplication.ps1"
    . $graphScriptPath

    $graphSecret = if ($askForSecretInput -eq $true) {$manualSecrets['GraphSecret']} else {"not-set"}
    Set-GraphAppKeyVaultSecrets `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -AppTenantId $AppTenantId `
        -ApplicationClientId $graphAppId `
        -CreateNewSecret $createNewSecrets `
        -CertificateName $GraphAppCertificateName `
        -AppSecret $graphSecret

    Write-Host "✅ Graph Application secrets saved" -ForegroundColor Green

    # Teams Channel Application Secrets
    Write-Host "`n📝 Saving Teams Channel Application secrets..." -ForegroundColor Yellow

    $teamsScriptPath = Join-Path $applicationSetupScriptsDirectory "Set-TeamsChannelAzureADApplication.ps1"
    . $teamsScriptPath

    $teamsChannelSecret = if ($askForSecretInput -eq $true) {$manualSecrets['TeamsChannelSecret']} else {"not-set"}
    Set-TeamsChannelAppKeyVaultSecrets `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -AppTenantId $AppTenantId `
        -ApplicationClientId $teamsChannelAppId `
        -CreateNewSecret $createNewSecrets `
        -CertificateName $TeamsChannelAppCertificateName `
        -AppSecret $teamsChannelSecret

    Write-Host "✅ Teams Channel Application secrets saved" -ForegroundColor Green

    Write-Host "`n✅ All app registration secrets have been saved to Key Vault" -ForegroundColor Green
    Write-Host "═══════════════════════════════════════════════════════════════════════════`n" -ForegroundColor Cyan
}

function Set-GMMAppRegistrationsManually {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$ScriptsDirectory,
        [Parameter(Mandatory = $true)]
        [boolean]$IsClientSecretAuth,
        [Parameter(Mandatory = $false)]
        [string]$DirectoryTenantId
    )

    Write-Host "`n⚠️  MANUAL APP REGISTRATION SETUP REQUIRED" -ForegroundColor Yellow
    Write-Host "═══════════════════════════════════════════════════════════════════════════" -ForegroundColor Yellow
    Write-Host "`nYou have chosen to skip privileged directory actions. This means you need to" -ForegroundColor White
    Write-Host "manually create the app registrations or run the setup scripts in a separate" -ForegroundColor White
    Write-Host "PowerShell session with appropriate permissions.`n" -ForegroundColor White

    Write-Host "📋 Required App Registrations:" -ForegroundColor Cyan
    Write-Host "   1. UI Application          ($SolutionAbbreviation-ui-$EnvironmentAbbreviation)" -ForegroundColor White
    Write-Host "   2. WebAPI Application      ($SolutionAbbreviation-webapi-$EnvironmentAbbreviation)" -ForegroundColor White
    Write-Host "   3. Graph Application       ($SolutionAbbreviation-Graph-$EnvironmentAbbreviation)" -ForegroundColor White
    Write-Host "   4. Teams Channel App       ($SolutionAbbreviation-TeamsChannel-$EnvironmentAbbreviation)" -ForegroundColor White

    Write-Host "`n📖 Manual Setup Documentation:" -ForegroundColor Cyan
    Write-Host "   Please refer to the following documentation for manual setup steps:" -ForegroundColor White
    Write-Host "   - $ScriptsDirectory/ApplicationSetupScripts/Manual Setup Documentation/UI-Application-Creation-Instructions.md" -ForegroundColor Gray
    Write-Host "   - $ScriptsDirectory/ApplicationSetupScripts/Manual Setup Documentation/WebAPI-Application-Creation-Instructions.md" -ForegroundColor Gray
    Write-Host "   - $ScriptsDirectory/ApplicationSetupScripts/Manual Setup Documentation/GraphCredentials-Application-Creation-Instructions.md" -ForegroundColor Gray
    Write-Host "   - $ScriptsDirectory/ApplicationSetupScripts/Manual Setup Documentation/TeamsChannel-Application-Creation-Instructions.md" -ForegroundColor Gray

    Write-Host "`n🔧 PowerShell Script Signatures:" -ForegroundColor Cyan
    Write-Host "   If you prefer to run the setup scripts, use these commands in a separate" -ForegroundColor White
    Write-Host "   PowerShell session with Global Administrator or Application Administrator permissions:`n" -ForegroundColor White

    Write-Host "   ⚠️  IMPORTANT: Install Required Modules First!" -ForegroundColor Yellow
    Write-Host "   Before running any of the setup scripts below, you must first install the required" -ForegroundColor White
    Write-Host "   PowerShell modules. Run these commands in your PowerShell session:`n" -ForegroundColor White

    Write-Host "   # Install Required Modules" -ForegroundColor Magenta
    Write-Host "   . `"$ScriptsDirectory/Install-AzModuleIfNeeded.ps1`"" -ForegroundColor Gray
    Write-Host "   Install-AzModuleIfNeeded" -ForegroundColor Gray
    Write-Host "" -ForegroundColor Gray
    Write-Host "   . `"$ScriptsDirectory/Install-ModuleIfNeeded.ps1`"" -ForegroundColor Gray
    Write-Host "   Install-ModuleIfNeeded -Name `"Microsoft.Graph.Authentication`" -Version `"2.17.0`"" -ForegroundColor Gray
    Write-Host "   Install-ModuleIfNeeded -Name `"Microsoft.Graph.Applications`" -Version `"2.17.0`"" -ForegroundColor Gray
    Write-Host "   Install-ModuleIfNeeded -Name `"Microsoft.Graph.Identity.DirectoryManagement`" -Version `"2.17.0`"" -ForegroundColor Gray
    Write-Host "   Install-ModuleIfNeeded -Name `"Microsoft.Graph.Users`" -Version `"2.17.0`"" -ForegroundColor Gray
    Write-Host "" -ForegroundColor Gray
    Write-Host "   `$global:SkipModuleInstall = `$true`n" -ForegroundColor Gray

    Write-Host "   Once the modules are installed, proceed with the app registration scripts:`n" -ForegroundColor White

    Write-Host "   # 1. UI Application" -ForegroundColor Green
    Write-Host "   . `"$ScriptsDirectory/ApplicationSetupScripts/Set-UIAzureADApplication.ps1`"" -ForegroundColor Gray
    Write-Host "   Set-UIAzureADApplication ``" -ForegroundColor Gray
    Write-Host "       -SolutionAbbreviation `"$SolutionAbbreviation`" ``" -ForegroundColor Gray
    Write-Host "       -EnvironmentAbbreviation `"$EnvironmentAbbreviation`" ``" -ForegroundColor Gray
    Write-Host "       -AppTenantId `"$DirectoryTenantId`" ``" -ForegroundColor Gray
    Write-Host "       -SaveToKeyVault `$false ``" -ForegroundColor Gray
    Write-Host "       -SkipIfApplicationExists `$false ``" -ForegroundColor Gray
    Write-Host "       -Clean `$false`n" -ForegroundColor Gray

    Write-Host "   # 2. WebAPI Application" -ForegroundColor Green
    Write-Host "   . `"$ScriptsDirectory/ApplicationSetupScripts/Set-WebApiAzureADApplication.ps1`"" -ForegroundColor Gray
    Write-Host "   Set-WebApiAzureADApplication ``" -ForegroundColor Gray
    Write-Host "       -SolutionAbbreviation `"$SolutionAbbreviation`" ``" -ForegroundColor Gray
    Write-Host "       -EnvironmentAbbreviation `"$EnvironmentAbbreviation`" ``" -ForegroundColor Gray
    Write-Host "       -AppTenantId `"$DirectoryTenantId`" ``" -ForegroundColor Gray
    Write-Host "       -SaveToKeyVault `$false ``" -ForegroundColor Gray
    Write-Host "       -SkipIfApplicationExists `$false ``" -ForegroundColor Gray
    Write-Host "       -Clean `$false`n" -ForegroundColor Gray

    Write-Host "   # 3. Graph Application" -ForegroundColor Green
    Write-Host "   . `"$ScriptsDirectory/ApplicationSetupScripts/Set-GraphCredentialsAzureADApplication.ps1`"" -ForegroundColor Gray
    Write-Host "   Set-GraphCredentialsAzureADApplication ``" -ForegroundColor Gray
    Write-Host "       -SolutionAbbreviation `"$SolutionAbbreviation`" ``" -ForegroundColor Gray
    Write-Host "       -EnvironmentAbbreviation `"$EnvironmentAbbreviation`" ``" -ForegroundColor Gray
    Write-Host "       -AppTenantId `"$DirectoryTenantId`" ``" -ForegroundColor Gray
    Write-Host "       -SaveToKeyVault `$false ``" -ForegroundColor Gray
    Write-Host "       -SkipIfApplicationExists `$false ``" -ForegroundColor Gray
    Write-Host "       -Clean `$false`n" -ForegroundColor Gray

    Write-Host "   # 4. Teams Channel Application" -ForegroundColor Green
    Write-Host "   . `"$ScriptsDirectory/ApplicationSetupScripts/Set-TeamsChannelAzureADApplication.ps1`"" -ForegroundColor Gray
    Write-Host "   Set-TeamsChannelAzureADApplication ``" -ForegroundColor Gray
    Write-Host "       -SolutionAbbreviation `"$SolutionAbbreviation`" ``" -ForegroundColor Gray
    Write-Host "       -EnvironmentAbbreviation `"$EnvironmentAbbreviation`" ``" -ForegroundColor Gray
    Write-Host "       -AppTenantId `"$DirectoryTenantId`" ``" -ForegroundColor Gray
    Write-Host "       -SaveToKeyVault `$false ``" -ForegroundColor Gray
    Write-Host "       -SkipIfApplicationExists `$false ``" -ForegroundColor Gray
    Write-Host "       -Clean `$false`n" -ForegroundColor Gray

    if ($IsClientSecretAuth -eq $true) {
        Write-Host "`n═══════════════════════════════════════════════════════════════════════════" -ForegroundColor Cyan
        Write-Host "🔐 Creating Application Secrets (Client Secret Authentication)" -ForegroundColor Yellow
        Write-Host "═══════════════════════════════════════════════════════════════════════════" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "You need to manually create a client secret for each application registration." -ForegroundColor White
        Write-Host ""
        Write-Host "⚠️  IMPORTANT: Save the secret values immediately after creation!" -ForegroundColor Yellow
        Write-Host "   You will be prompted to input these secrets later in this deployment." -ForegroundColor Yellow
        Write-Host "   Secret values cannot be retrieved after you navigate away from the creation screen." -ForegroundColor Yellow
        Write-Host ""
        Write-Host "📖 Documentation Location:" -ForegroundColor Cyan
        Write-Host "   $ScriptsDirectory/ApplicationSetupScripts/Manual Setup Documentation" -ForegroundColor Gray
        Write-Host ""
        Write-Host "Each application folder contains detailed instructions on creating client secrets." -ForegroundColor White
        Write-Host ""
    }

    Write-Host "═══════════════════════════════════════════════════════════════════════════" -ForegroundColor Yellow
    Write-Host "`n⏸️  Once you have completed the app registrations setup, press ENTER to continue..." -ForegroundColor Cyan
    Write-Host "   (The script will validate all app registrations before proceeding)`n" -ForegroundColor Gray
    
    $null = Read-Host

    Write-Host "`n🔍 Validating App Registrations..." -ForegroundColor Cyan
    Write-Host "═══════════════════════════════════════════════════════════════════════════`n" -ForegroundColor Cyan

    # Source the validation scripts
    . ($ScriptsDirectory + '/ApplicationSetupScripts/Set-UIAzureADApplication.ps1')
    . ($ScriptsDirectory + '/ApplicationSetupScripts/Set-WebApiAzureADApplication.ps1')
    . ($ScriptsDirectory + '/ApplicationSetupScripts/Set-GraphCredentialsAzureADApplication.ps1')
    . ($ScriptsDirectory + '/ApplicationSetupScripts/Set-TeamsChannelAzureADApplication.ps1')

    # Validate each application
    $uiValid = Test-UIApplication -SolutionAbbreviation $SolutionAbbreviation -EnvironmentAbbreviation $EnvironmentAbbreviation
    $webApiValid = Test-WebApiApplication -SolutionAbbreviation $SolutionAbbreviation -EnvironmentAbbreviation $EnvironmentAbbreviation
    $graphValid = Test-GraphCredentialsApplication -SolutionAbbreviation $SolutionAbbreviation -EnvironmentAbbreviation $EnvironmentAbbreviation
    $teamsChannelValid = Test-TeamsChannelApplication -SolutionAbbreviation $SolutionAbbreviation -EnvironmentAbbreviation $EnvironmentAbbreviation

    Write-Host "`n═══════════════════════════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "📊 Validation Summary:" -ForegroundColor Cyan
    Write-Host "   UI Application:           $(if ($uiValid) { '✅ PASS' } else { '❌ FAIL' })" -ForegroundColor $(if ($uiValid) { 'Green' } else { 'Red' })
    Write-Host "   WebAPI Application:       $(if ($webApiValid) { '✅ PASS' } else { '❌ FAIL' })" -ForegroundColor $(if ($webApiValid) { 'Green' } else { 'Red' })
    Write-Host "   Graph Application:        $(if ($graphValid) { '✅ PASS' } else { '❌ FAIL' })" -ForegroundColor $(if ($graphValid) { 'Green' } else { 'Red' })
    Write-Host "   Teams Channel Application: $(if ($teamsChannelValid) { '✅ PASS' } else { '❌ FAIL' })" -ForegroundColor $(if ($teamsChannelValid) { 'Green' } else { 'Red' })
    Write-Host "═══════════════════════════════════════════════════════════════════════════`n" -ForegroundColor Cyan

    if (-not ($uiValid -and $webApiValid -and $graphValid -and $teamsChannelValid)) {
        Write-Host "❌ One or more applications failed validation. Please review the errors above and fix the issues." -ForegroundColor Red
        Write-Host "   You can re-run the validation by calling the Test-*Application functions individually.`n" -ForegroundColor Yellow
        throw "App registration validation failed. Please fix the issues and try again."
    }

    Write-Host "✅ All app registrations validated successfully!`n" -ForegroundColor Green

    # Retrieve application details to check for admin consent requirements
    $uiApp = Get-MgApplication -Filter "displayName eq '$SolutionAbbreviation-ui-$EnvironmentAbbreviation'"
    $webApiApp = Get-MgApplication -Filter "displayName eq '$SolutionAbbreviation-webapi-$EnvironmentAbbreviation'"
    $graphApp = Get-MgApplication -Filter "displayName eq '$SolutionAbbreviation-Graph-$EnvironmentAbbreviation'"
    $teamsChannelApp = Get-MgApplication -Filter "displayName eq '$SolutionAbbreviation-TeamsChannel-$EnvironmentAbbreviation'"

    # Check which apps need admin consent based on their required resource access
    $appsThatNeedAdminConsent = @()

    $apps = @(
        $uiApp,
        $webApiApp,
        $graphApp,
        $teamsChannelApp
    )

    foreach ($app in $apps) {
        if ($null -eq $app) {
            throw "Application '$($app.DisplayName)' not found after validation. Please ensure it was created correctly."
        }

        $appsThatNeedAdminConsent += @{
            ApplicationId   = $app.AppId;
            ApplicationName = $app.DisplayName
        }
    }

    return @{
        UIAppId = $uiApp.AppId
        WebApiAppId = $webApiApp.AppId
        GraphAppId = $graphApp.AppId
        TeamsChannelAppId = $teamsChannelApp.AppId
        AppsThatNeedAdminConsent = $appsThatNeedAdminConsent
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
        [Parameter(Mandatory = $true)]
        [boolean] $IsClientSecretAuth,
        [Parameter(Mandatory = $false)]
        [string]$DirectoryTenantId,
        [Parameter(Mandatory = $true)]
        [boolean] $SkipPrivilegedDirectoryActions
    )

    Write-Host "`n╔════════════════════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "║          Creating GMM App Registrations                                    ║" -ForegroundColor Cyan
    Write-Host "╚════════════════════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan

    $appCreationResult = $null
    if ($SkipPrivilegedDirectoryActions -eq $true) {
        # Manual flow - prompt user to create app registrations
        $appCreationResult = Set-GMMAppRegistrationsManually `
            -SolutionAbbreviation $SolutionAbbreviation `
            -EnvironmentAbbreviation $EnvironmentAbbreviation `
            -ScriptsDirectory $ScriptsDirectory `
            -IsClientSecretAuth $IsClientSecretAuth `
            -DirectoryTenantId $DirectoryTenantId
    }
    else {
        # Normal flow - create app registrations programmatically
        $appCreationResult = Set-GMMAppRegistrationsProgrammatically `
            -SolutionAbbreviation $SolutionAbbreviation `
            -EnvironmentAbbreviation $EnvironmentAbbreviation `
            -ScriptsDirectory $ScriptsDirectory `
            -DirectoryTenantId $DirectoryTenantId
    }

    Write-Host "✅ App registrations created successfully!`n" -ForegroundColor Green
    return $appCreationResult
}

function Show-ManualRedirectURIInstructions {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$UIAppRegistrationId,
        [Parameter(Mandatory = $true)]
        [string[]]$RedirectUris
    )

    # Construct the direct link to the app registration's Authentication blade
    $portalLink = "https://portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationMenuBlade/~/Authentication/appId/$UIAppRegistrationId/isMSAApp~/false"

    Write-Host "`n⚠️  MANUAL REDIRECT URI UPDATE REQUIRED" -ForegroundColor Yellow
    Write-Host "═══════════════════════════════════════════════════════════════════════════" -ForegroundColor Yellow
    Write-Host "`nThe following redirect URIs need to be added to the UI application:" -ForegroundColor White
    Write-Host "Application: $SolutionAbbreviation-ui-$EnvironmentAbbreviation" -ForegroundColor Cyan
    Write-Host "Application (client) ID: $UIAppRegistrationId`n" -ForegroundColor Cyan
    
    Write-Host "🔗 Direct link to app registration:" -ForegroundColor Cyan
    Write-Host "   $portalLink`n" -ForegroundColor White
    
    Write-Host "📋 Redirect URIs to add:" -ForegroundColor Cyan
    foreach ($uri in $RedirectUris) {
        Write-Host "   • $uri" -ForegroundColor White
    }
    
    Write-Host "`n📖 Manual Steps:" -ForegroundColor Cyan
    Write-Host "   1. Click the direct link above or go to: https://portal.azure.com" -ForegroundColor White
    Write-Host "   2. If using the portal link directly:" -ForegroundColor White
    Write-Host "      - Navigate to Microsoft Entra ID > App registrations" -ForegroundColor White
    Write-Host "      - Find and select: $SolutionAbbreviation-ui-$EnvironmentAbbreviation" -ForegroundColor White
    Write-Host "      - Go to 'Authentication' in the left menu" -ForegroundColor White
    Write-Host "   3. Under 'Single-page application', click 'Add URI'" -ForegroundColor White
    Write-Host "   4. Add each of the redirect URIs listed above" -ForegroundColor White
    Write-Host "   5. Click 'Save' at the top of the page`n" -ForegroundColor White
    
    Write-Host "═══════════════════════════════════════════════════════════════════════════" -ForegroundColor Yellow
    Write-Host "⏸️  Press ENTER once you have added the redirect URIs..." -ForegroundColor Cyan
    $null = Read-Host
}

function Set-ConfigureWebApps {
    param (
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)]
        [AllowNull()]
        [AllowEmptyString()]
        [string]$UIAppRegistrationId,
        [Parameter(Mandatory = $true)]
        [boolean]$SkipPrivilegedDirectoryActions        
    )

    Write-Host "`n🔧 Configuring Web Apps and App Registrations for CORS..." -ForegroundColor Cyan

    $computeResourceGroup = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation"
    $webApiName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-webapi"
    $uiWebAppName = "$SolutionAbbreviation-ui"

    # Set CORS for web apps
    $allowedOrigins = @()

    try {
        $customDomain = Get-AzStaticWebAppCustomDomain -Name $uiWebAppName -ResourceGroupName $computeResourceGroup
        if (-not [string]::IsNullOrEmpty($customDomain)) {
            $allowedOrigins += "https://$($customDomain.DomainName)"
        }
    }
    catch {
        Write-Output "No custom domain associated with this web app."
    }

    $staticWebApp = Get-AzStaticWebApp -Name $uiWebAppName -ResourceGroupName $computeResourceGroup
    $allowedOrigins += "https://$($staticWebApp.DefaultHostname)"

    # Set CORS for SignalR service
    try {
        Update-AzSignalR `
            -ResourceGroupName $computeResourceGroup `
            -Name "$computeResourceGroup-signalr" `
            -AllowedOrigin $allowedOrigins

        Write-Host "✅ SignalR service CORS settings updated successfully" -ForegroundColor Green
    }
    catch {
        Write-Output "Unable to update SignalR service CORS settings."
    }

    $webApi = Get-AzWebApp -ResourceGroupName $computeResourceGroup -Name $webApiName
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
            ResourceName      = $webApiName
            ResourceType      = "Microsoft.Web/sites"
            ResourceGroupName = $computeResourceGroup
        }

        $webApiResource = Get-AzResource @apiResourceParams
        $webApiResource.Properties.siteConfig.cors = @{
            allowedOrigins = $newCORs
        }

        $webApiResource | Set-AzResource -Force

        Write-Host "✅ WebAPI CORS settings updated successfully" -ForegroundColor Green
    }
    else {
        Write-Host "No new CORS origins to add to WebAPI" -ForegroundColor Gray
    }

    # Retrieve UI App Registration ID from Key Vault if not provided
    if ([string]::IsNullOrWhiteSpace($UIAppRegistrationId)) {
        Write-Host "UI App Registration ID not provided. Retrieving from Key Vault..." -ForegroundColor Yellow
        $UIAppRegistrationId = Get-KeyVaultSecretWithFirewallRetry `
            -ResourceGroup "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation" `
            -VaultName "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation" `
            -SecretName "uiAppId" `
            -AsPlainText
        
        if ([string]::IsNullOrWhiteSpace($UIAppRegistrationId)) {
            Write-Error "Unable to retrieve UI App Registration ID from Key Vault"
            return
        }
        Write-Host "  ✓ Retrieved UI App ID: $UIAppRegistrationId" -ForegroundColor Gray
    }

    $uiApp = Get-MgApplication -Filter "appId eq '$UIAppRegistrationId'"
    $currentRedirectUris = Get-Default -Value $uiApp.Spa.RedirectUris -Default @()
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

        if ($SkipPrivilegedDirectoryActions -eq $true) {
            # Manual mode - provide instructions
            Show-ManualRedirectURIInstructions `
                -SolutionAbbreviation $SolutionAbbreviation `
                -EnvironmentAbbreviation $EnvironmentAbbreviation `
                -UIAppRegistrationId $UIAppRegistrationId `
                -RedirectUris $newRedirectUris
        }
        else {
            # Automated mode - update via Microsoft Graph
            Write-Host "Updating UI application redirect URIs..." -ForegroundColor Yellow
            Update-MgApplication `
                -ApplicationId $uiApp.Id `
                -Spa @{ RedirectUris = $newRedirectUris }
            Write-Host "✅ Redirect URIs updated successfully" -ForegroundColor Green
        }
    }
}
function Set-PublishUICode {
    param (
        [Parameter(Mandatory = $true)]
        [AllowNull()]
        [AllowEmptyString()]
        [string]$UIAppClientId,
        [Parameter(Mandatory = $true)]
        [string]$DirectoryTenantId,
        [Parameter(Mandatory = $true)]
        [AllowNull()]
        [AllowEmptyString()]
        [string]$WebApiAppClientId,
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,
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

    Write-Host "Publishing UI code to Azure Static Web App..." -ForegroundColor Yellow

    $dataResourceGroup = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $computeResourceGroup = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation"
    $prereqsResourceGroup = "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation"
    $prereqsKeyVaultName = "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation"
    $webApiBaseUri = "https://$SolutionAbbreviation-compute-$EnvironmentAbbreviation-webapi.azurewebsites.net"

    if ([string]::IsNullOrWhiteSpace($UIAppClientId)) {
        $UIAppClientId = Get-KeyVaultSecretWithFirewallRetry `
                        -ResourceGroup $prereqsResourceGroup `
                        -VaultName $prereqsKeyVaultName `
                        -SecretName "uiAppId" `
                        -AsPlainText
    }

    if ([string]::IsNullOrWhiteSpace($WebApiAppClientId)) {
        $WebApiAppClientId = Get-KeyVaultSecretWithFirewallRetry `
                            -ResourceGroup $prereqsResourceGroup `
                            -VaultName $prereqsKeyVaultName `
                            -SecretName "webApiClientId" `
                            -AsPlainText
    } 
    
    $appInsights = Get-AzApplicationInsights -ResourceGroupName $dataResourceGroup  -Name "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $appInsightsConnectionString = $appInsights.ConnectionString

    $buildVersionFilePath = "$WebAppDirectory/buildVersion.txt"
    if (Test-Path -Path $buildVersionFilePath) {
        $buildVersion = Get-Content -Path $buildVersionFilePath
        Write-Host "Build version: $buildVersion"
    }

    $envContent = "REACT_APP_AAD_UI_APP_CLIENT_ID=$UIAppClientId`n"
    $envContent += "REACT_APP_AAD_APP_TENANT_ID=$DirectoryTenantId`n"
    $envContent += "REACT_APP_AAD_API_APP_CLIENT_ID=$WebApiAppClientId`n"
    $envContent += "REACT_APP_AAD_APP_SERVICE_BASE_URI=$webApiBaseUri`n"
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
    $webAppSecrets = (Get-AzStaticWebAppSecret -name $webAppName -ResourceGroupName $computeResourceGroup).Property | ConvertFrom-Json
    $webAppDeploymentToken = $webAppSecrets.apiKey

    swa build
    swa deploy "build" --env "Production" -n $webAppName -R $computeResourceGroup --deployment-token $webAppDeploymentToken

    Set-Location -Path $currentLocation

    Write-Host "✅ UI code published successfully!" -ForegroundColor Green
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
        [Parameter(Mandatory = $true)]
        [bool]$SkipPrivilegedDirectoryActions,
        [Parameter(Mandatory = $false)]
        [bool]$AssertUserPermissions = $true
    )

    Test-ScriptDependencies

    if ($SkipModuleInstallation -eq $true) {
        Write-Host "Skipping module installation as per configuration [SkipModuleInstallation = $($SkipModuleInstallation)]." -ForegroundColor Yellow
    } else {
        Write-Host "Installing required PowerShell modules..."
        Install-RequiredModules -ScriptsDirectory $ScriptsDirectory
    }

    # Connect to Microsoft Graph with required scopes
    $requiredScopes = @()

    if ($SkipPrivilegedDirectoryActions -eq $true) {
        $requiredScopes = @(
            "Application.Read.All"
        )
    }
    else {
        $requiredScopes = @(
            "AppRoleAssignment.ReadWrite.All",
            "Directory.ReadWrite.All", 
            "Application.ReadWrite.All"
        )
    }

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
        if (-not $SkipPrivilegedDirectoryActions) {
            . ($ScriptsDirectory + '/Assert-MicrosoftGraphPermissions.ps1')
            Assert-MicrosoftGraphPermissions
        }

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
    $isInitialDeployment                            = $parameterHashtable.isInitialDeployment.value
    $resetGMMType                                   = $parameterHashtable.resetGMMType.value
    $useDeviceAuthentication                        = $parameterHashtable.useDeviceAuthentication.value
    $skipModuleInstallation                         = $parameterHashtable.skipModuleInstallation.value

    $setRBACPermissions             = Get-Default -Value $ParameterHashtable['setRBACPermissions'].value      -Default $false
    $skipSqlServerPermissionSetup   = Get-Default -Value $ParameterHashtable['skipSqlServerPermissionSetup'].value -Default $false
    $skipPrivilegedDirectoryActions   = Get-Default -Value $ParameterHashtable['skipPrivilegedDirectoryActions'].value -Default $false
    $tenantDomain                   = Get-DefaultString -Value $ParameterHashtable['tenantDomain'].value                   -Default 'not-set'
    $sharepointDomain               = Get-DefaultString -Value $ParameterHashtable['sharepointDomain'].value               -Default 'not-set'
    $directoryTenantId              = Get-DefaultString -Value $ParameterHashtable['directoryTenantId'].value -Default $parameterHashtable.tenantId.value

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
        -SkipPrivilegedDirectoryActions $skipPrivilegedDirectoryActions `
        -AssertUserPermissions $assertUserPermissions

    if (!$skipResourceProvidersCheck) {
        Set-ResourceProviders
    }

    if ($parameterHashtable.skipAppRegistrationSetup.value -ne $true) {
        $isClientSecretAuth = if ($ParameterHashtable.authenticationType.value -eq "ClientSecret") { $true } else { $false }
        $appRegistrationSetupResult = Set-GMMAppRegistrations `
                                        -SolutionAbbreviation $solutionAbbreviation `
                                        -EnvironmentAbbreviation $environmentAbbreviation `
                                        -ScriptsDirectory $scriptsDirectory `
                                        -IsClientSecretAuth $isClientSecretAuth `
                                        -DirectoryTenantId $directoryTenantId `
                                        -SkipPrivilegedDirectoryActions $skipPrivilegedDirectoryActions
    }
    else {
        Write-Host "`nSkipping App Registration setup as per configuration [skipAppRegistrationSetup = $($parameterHashtable.skipAppRegistrationSetup.value)]." -ForegroundColor Yellow
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
            -ADFDBConnectionString $connectionStringADF `
            -SetRBACPermissions $setRBACPermissions
    }


    # Import reusable functions
    . ($scriptsDirectory + '/ReusableModules/Get-KeyVaultSecretWithFirewallRetry.ps1')
    . ($scriptsDirectory + '/ReusableModules/Set-KeyVaultSecretWithFirewallRetry.ps1')
    . ($scriptsDirectory + '/ReusableModules/Invoke-SqlOperationWithFirewallRetry.ps1')

    Set-GMMResources `
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
        $isUserAssignedManagedIdentityAuth = if ($ParameterHashtable.authenticationType.value -eq "UserAssignedManagedIdentity") { $true } else { $false }
        Set-RBACPermissions `
        -SolutionAbbreviation $solutionAbbreviation `
        -EnvironmentAbbreviation $environmentAbbreviation `
        -TenantId $parameterHashtable.tenantId.value `
        -ScriptsDirectory "$scriptsDirectory/PostDeploymentRoleAssignments" `
        -SetUserAssignedManagedIdentityPermissions $isUserAssignedManagedIdentityAuth `
        -SkipPrivilegedDirectoryActions $skipPrivilegedDirectoryActions
    }

    Set-FunctionAppCode `
        -ComputeResourceGroup $computeResourceGroup `
        -FunctionsPackagesDirectory "$deploymentPackageDirectory/function_packages" `
        -WebApiPackagesDirectory "$deploymentPackageDirectory/webapi_package"

    # Configure web apps
    if ($parameterHashtable.skipAppRegistrationSetup.value -ne $true) {
        Set-ConfigureWebApps `
            -SolutionAbbreviation $solutionAbbreviation `
            -EnvironmentAbbreviation $environmentAbbreviation `
            -UIAppRegistrationId $appRegistrationSetupResult.UIAppId `
            -SkipPrivilegedDirectoryActions $skipPrivilegedDirectoryActions
    }
    else {
        Write-Host "`nSkipping Web App configuration as per configuration [skipAppRegistrationSetup = $($parameterHashtable.skipAppRegistrationSetup.value)]." -ForegroundColor Yellow
    }

    $uiAppClientId = if ($appRegistrationSetupResult -ne $null) { $appRegistrationSetupResult.UIAppId } else { $null }
    $webApiAppClientId = if ($appRegistrationSetupResult -ne $null) { $appRegistrationSetupResult.WebApiAppId } else { $null }

    # Publish UI code
    Set-PublishUICode `
        -UIAppClientId $uiAppClientId `
        -DirectoryTenantId $directoryTenantId `
        -WebApiAppClientId $webApiAppClientId `
        -SolutionAbbreviation $solutionAbbreviation `
        -EnvironmentAbbreviation $environmentAbbreviation `
        -WebAppDirectory "$deploymentPackageDirectory/webapp_package/web-app" `
        -MainTenantId $parameterHashtable.tenantId.value `
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

        if ($parameterHashtable.skipPrivilegedDirectoryActions.value -eq $true) {
            Show-WebAPIResetAdministratorInstructions `
                -SolutionAbbreviation $solutionAbbreviation `
                -EnvironmentAbbreviation $environmentAbbreviation
        }
        else {
             Set-WebAPIAsResetAdministrator `
                -SolutionAbbreviation $solutionAbbreviation `
                -EnvironmentAbbreviation $environmentAbbreviation
        }

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

    if ($parameterHashtable.skipAppRegistrationSetup -ne $true -and $appRegistrationSetupResult -ne $null -and $appRegistrationSetupResult.AppsThatNeedAdminConsent.Count -gt 0) {
        Write-Host "`n==========================================================" -ForegroundColor Yellow
        Write-Host "The following applications might require admin consent:" -ForegroundColor Yellow
        Write-Host "==========================================================" -ForegroundColor Yellow

        foreach ($app in $appRegistrationSetupResult.AppsThatNeedAdminConsent) {
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