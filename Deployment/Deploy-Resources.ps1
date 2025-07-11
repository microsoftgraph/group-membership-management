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
        if ($ParametersObject.parameters.Keys -contains $_) {
            $commonParametersObject[$_] = @{ value = $ParametersObject.parameters[$_].value }
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

function Get-BearerToken {
    param (
        [string]$Resource = "https://management.azure.com/"
    )

    $token = (Get-AzAccessToken -ResourceUrl $Resource).Token
    if ($token -is [System.Security.SecureString]) {
        $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($token)
        try {
            $plainToken = [Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr)
        }
        finally {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
        }
    }
    else {
        $plainToken = $token
    }
    return $plainToken
}

function Start-ResourceDeployment {
    param (
        [Parameter(Mandatory = $true)][string]$SubscriptionId,
        [Parameter(Mandatory = $true)][string]$TemplateFilePath,
        [Parameter(Mandatory = $true)][string]$ParameterFilePath,
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
    Write-Host "Using parameter file: $ParameterFilePath"

    if (-not (Test-Path $TemplateFilePath)) { throw "Template file not found at path: $TemplateFilePath" }
    if (-not (Test-Path $ParameterFilePath)) { throw "Parameter file not found at path: $ParameterFilePath" }

    $deploymentName = "deployment-$(Get-Date -Format yyyyMMddHHmmss)"
    $templateContent = Get-TemplateAsHashtable -TemplateFilePath $TemplateFilePath
    $templateParameters = Get-TemplateParameters `
        -TemplateFilePath $TemplateFilePath `
        -ParametersFilePath $ParameterFilePath `
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
        } | ConvertTo-Json -Depth 30
        $baseUri = "https://management.azure.com/subscriptions/$SubscriptionId/providers/Microsoft.Resources/deployments/$deploymentName"
    }
    else {
        $body = @{
            properties = @{
                mode = 'Incremental'
                template = $templateContent
                parameters = $templateParameters
            }
        } | ConvertTo-Json -Depth 30
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

        $maxAttempts = 50
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
        [string]$ParameterFilePath,
        [Parameter(Mandatory = $false)]
        [Hashtable]$AdditionalParameters = @{ parameters = @{} },
        [Parameter(Mandatory = $false)]
        [bool] $SetRBACPermissions
    )
    
    Write-Host "`nCreating resource groups:"
    $templateFilePath = "$ResourceGroupTemplateDirectoryPath\resourceGroups.json"
    Retry-Operation `
        -Operation ${function:Start-ResourceDeployment} `
        -OperationName "Create Resource Groups" `
        -params @{
        SubscriptionId          = $SubscriptionId
        Location                = $Location
        TemplateFilePath        = $templateFilePath
        ParameterFilePath       = $ParameterFilePath
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
        [string]$ParameterFilePath,
        [Parameter(Mandatory = $false)]
        [Hashtable]$AdditionalParameters = @{ parameters = @{} },
        [Parameter(Mandatory = $false)]
        [bool] $SetRBACPermissions
    )

    Write-Host "`nCreating prereqs resources"
    $prereqsResourceGroup = "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation"
    $templateFilePath = "$PrereqsTemplateDirectoryPath\prereqResources.json"
    Retry-Operation `
        -Operation ${function:Start-ResourceDeployment} `
        -OperationName "Create prereqs resources" `
        -params @{
        ResourceGroupName       = $prereqsResourceGroup
        SubscriptionId          = $SubscriptionId
        TemplateFilePath        = $templateFilePath
        ParameterFilePath       = $ParameterFilePath
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
        [string]$ParameterFilePath,
        [Parameter(Mandatory = $false)]
        [Hashtable]$AdditionalParameters = @{ parameters = @{} },
        [Parameter(Mandatory = $false)]
        [bool] $SetRBACPermissions
    )
    
    Write-Host "`nCreating data resources"
    $dataResourceGroup = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $templateFilePath = "$DataTemplateDirectoryPath\dataResources.json"
    Retry-Operation `
        -Operation ${function:Start-ResourceDeployment} `
        -OperationName "Create data resources" `
        -params @{
        ResourceGroupName       = $dataResourceGroup
        SubscriptionId          = $SubscriptionId
        TemplateFilePath        = $templateFilePath
        ParameterFilePath       = $ParameterFilePath
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
        [string]$ParameterFilePath,
        [Parameter(Mandatory = $false)]
        [Hashtable]$AdditionalParameters = @{ parameters = @{} }
    )

    Write-Host "`nCreating compute resources"
    $computeResourceGroup = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation"
    $templateFilePath = "$ComputeTemplateDirectoryPath\computeResources.json"
    Retry-Operation `
        -Operation ${function:Start-ResourceDeployment} `
        -OperationName "Create compute resources" `
        -params @{
        ResourceGroupName       = $computeResourceGroup
        SubscriptionId          = $SubscriptionId
        TemplateFilePath        = $templateFilePath
        ParameterFilePath       = $ParameterFilePath
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
        [string]$ParameterFilePath,
        [Parameter(Mandatory = $false)]
        [Hashtable]$AdditionalParameters = @{ parameters = @{} }
    )

    # Ensure ADF secrets are set in the Key Vault
    write-Host "`nEnsuring ADF secrets are set in the Key Vault"
    $adfDataSecrets = @("sqlAdminPassword", "azureUserReaderUrl", "azureUserReaderKey", "adfStorageAccountName")
    foreach ($secret in $adfDataSecrets) {
        $secretExists = Check-IfKeyVaultSecretExists -VaultName $dataResourceGroup -SecretName $secret
        if (-not $secretExists) {
            $secretValue = New-Object System.Security.SecureString
            "not-set".ToCharArray() | ForEach-Object { $secretValue.AppendChar($_) }
            Set-AzKeyVaultSecret -VaultName $dataResourceGroup -Name $secret -SecretValue $secretValue
        }
    }

    # Deploy ADF resources
    Write-Host "`nCreating ADF resources"
    $dataResourceGroup = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $templateFilePath = "$ADFTemplateDirectoryPath\adfHRResources.json"
    Retry-Operation `
        -Operation ${function:Start-ResourceDeployment} `
        -OperationName "Create ADF resources" `
        -params @{
        ResourceGroupName       = $dataResourceGroup
        SubscriptionId          = $SubscriptionId
        TemplateFilePath        = $templateFilePath
        ParameterFilePath       = $ParameterFilePath
        AdditionalParameters    = $AdditionalParameters
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

    $setRBACPermissions      = Get-Default -Value $parameters['setRBACPermissions'].value      -Default $false
    $createAppRegistrations  = Get-Default -Value $parameters['createAppRegistrations'].value  -Default $true
    $applyDBMigrations       = Get-Default -Value $parameters['applyDBMigrations'].value       -Default $true
    $skipAppRegistrationSetupIfAppExists = Get-Default -Value $parameters['skipAppRegistrationSetupIfAppExists'].value -Default $false
    $setRBACPermissionsBicep = Get-Default -Value $parameters['setRBACPermissionsBicep'].value -Default $false
    $createResourceGroups = Get-Default -Value $parameters['createResourceGroups'].value -Default $false
    $skipAzureDataFactoryDeployment = Get-Default -Value $parameters['skipAzureDataFactoryDeployment'].value -Default $false
    $ipRangesToWhiteList = Get-Default -Value $parameters['IpRangesToWhiteList'].value -Default @()

    # strings
    $graphAppCertificateName        = Get-DefaultString -Value $parameters['graphAppCertificateName'].value        -Default 'not-set'
    $teamsChannelAppCertificateName = Get-DefaultString -Value $parameters['teamsChannelAppCertificateName'].value -Default 'not-set'
    $tenantDomain                   = Get-DefaultString -Value $parameters['tenantDomain'].value                   -Default 'not-set'
    $sharepointDomain               = Get-DefaultString -Value $parameters['sharepointDomain'].value               -Default 'not-set'
    $secondaryTenantId              = Get-DefaultString -Value $parameters['secondaryTenantId'].value -Default $null

    $hostIpAddress = (Invoke-WebRequest -uri "https://api.ipify.org/").Content
    $ipAddressesToWhiteList = $ipRangesToWhiteList + @($hostIpAddress)
    
    # deploy resource groups
    if ($createResourceGroups -eq $true) {
        Set-ResourceGroups `
            -SubscriptionId $SubscriptionId `
            -Location $Location `
            -ResourceGroupTemplateDirectoryPath $TemplateFilePath `
            -ParameterFilePath $ParameterFilePath `
            -AdditionalParameters $commonParametersObject `
            -SetRBACPermissions $setRBACPermissions
    }

    # deploy prereq resources
    Set-PrereqResources `
        -SolutionAbbreviation           $SolutionAbbreviation `
        -EnvironmentAbbreviation        $EnvironmentAbbreviation `
        -SubscriptionId                 $SubscriptionId `
        -PrereqsTemplateDirectoryPath   $TemplateFilePath `
        -ParameterFilePath              $ParameterFilePath `
        -AdditionalParameters           $commonParametersObject `
        -SetRBACPermissions             $setRBACPermissionsBicep

    Start-Sleep -Seconds 10

    Set-KeyVaultFirewallRules `
        -ResourceGroups @($prereqsResourceGroup) `
        -ipAddresses $ipAddressesToWhiteList `
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
    Set-DataResources `
        -SolutionAbbreviation       $SolutionAbbreviation `
        -EnvironmentAbbreviation    $EnvironmentAbbreviation `
        -SubscriptionId             $SubscriptionId `
        -DataTemplateDirectoryPath  $TemplateFilePath `
        -ParameterFilePath          $ParameterFilePath `
        -AdditionalParameters       $commonParametersObject `
        -SetRBACPermissions         $setRBACPermissionsBicep 

    Start-Sleep -Seconds 10

    Set-KeyVaultFirewallRules `
        -ResourceGroups @($dataResourceGroup) `
        -ipAddresses $ipAddressesToWhiteList `
        -ScriptsDirectory "$scriptsDirectory\Scripts" `
        -Region $Location
    
    # deploy compute resources
    Set-ComputeResources `
        -SolutionAbbreviation           $SolutionAbbreviation `
        -EnvironmentAbbreviation        $EnvironmentAbbreviation `
        -SubscriptionId                 $SubscriptionId `
        -ComputeTemplateDirectoryPath   $TemplateFilePath `
        -ParameterFilePath              $ParameterFilePath `
        -AdditionalParameters           $commonParametersObject

    Start-Sleep -Seconds 10

    # deploy ADF resources
    if ($skipAzureDataFactoryDeployment -eq $false) {
        Write-Host "`nCreating Azure Data Factory resources"
        Set-ADFResources `
            -SolutionAbbreviation       $SolutionAbbreviation `
            -EnvironmentAbbreviation    $EnvironmentAbbreviation `
            -SubscriptionId             $SubscriptionId `
            -ADFTemplateDirectoryPath   $TemplateFilePath `
            -ParameterFilePath          $ParameterFilePath `
            -AdditionalParameters       $commonParametersObject
        Start-Sleep -Seconds 10
    }
    else {
        Write-Host "`nSkipping Azure Data Factory deployment as per configuration."
    }

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
        -SetUserAssignedManagedIdentityPermissions $SetUserAssignedManagedIdentityPermissions `
        -InstallRequiredModules $false `
        -ConnectToMsGraph $false 

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
        [string[]]$ipAddresses,
        [Parameter(Mandatory = $true)]
        [string]$ScriptsDirectory,
        [Parameter(Mandatory = $true)]
        [string]$Region
    )

    Write-Host "Enabling firewall rules for key vaults"

    # Get IP rules from script
    . ($ScriptsDirectory + '\Get-FirewallIPRules.ps1') -FolderPathToSaveIpRules $ScriptsDirectory -Regions $Region
    $newIpRules = Get-Content "$ScriptsDirectory\ipRules.txt"
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
            $appsThatNeedAdminConsent += $appInfo.ApplicationName
        }
    }

    #return the response
    return @{
        UIApplicationId            = $uiInformation.ApplicationId;
        UITenantId                 = $uiInformation.TenantId;
        APIApplicationId           = $apiInformation.ApplicationId;
        APITenantId                = $apiInformation.TenantId;
        GraphApplicationId         = $graphInformation.ApplicationId;
        GraphTenantId              = $graphInformation.TenantId;
        TeamsChannelApplicationId  = $teamsChannelInformation.ApplicationId;
        TeamsChannelTenantId       = $teamsChannelInformation.TenantId;
        AppsThatNeedAdminConsent = $appsThatNeedAdminConsent;
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

    # MS Graph PowerShell modules
    Write-Host "Checking Microsoft Graph PowerShell modules..."
		
    $requiredGraphModules = @(
        "Microsoft.Graph.Authentication",
        "Microsoft.Graph.Applications",
        "Microsoft.Graph.Identity.DirectoryManagement",
        "Microsoft.Graph.Users"
    )

    . ($scriptsDirectory + '\scripts\Install-ModuleIfNeeded.ps1')

    foreach ($module in $requiredGraphModules) {
        Install-ModuleIfNeeded -Name $module -Version "2.17.0" -Verbose
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
        [bool]$AssertUserPermissions = $true,
        [Parameter(Mandatory = $false)]
        [bool] $SetUserAssignedManagedIdentityPermissions = $true,
        [Parameter(Mandatory = $false)]
        [System.Nullable[Guid]]$SecondaryTenantId,
        [Parameter(Mandatory = $false)]
        [bool]$IsInitialDeployment = $false,
        [Parameter(Mandatory = $false)]
        [ValidateSet("Credentials", "ServicePrincipal","Skip")]
        [string]$ResetGMMType = "Skip"
    )

    Test-ScriptDependencies

    $scriptsDirectory = Split-Path $PSScriptRoot -Parent

    Set-Subscription `
        -ScriptsDirectory "$scriptsDirectory\scripts" `
        -SubscriptionId $SubscriptionId

    if ($AssertUserPermissions -eq $true) {
        . ($scriptsDirectory + '\scripts\Assert-RbacPermissionsForDeployment.ps1')
        Assert-RbacPermissionsForDeployment `
            -SolutionAbbreviation $SolutionAbbreviation `
            -EnvironmentAbbreviation $EnvironmentAbbreviation 

        . ($scriptsDirectory + '\scripts\Assert-MicrosoftGraphPermissions.ps1')
        Assert-MicrosoftGraphPermissions
    }
    
    # define the resource groups
    $dataResourceGroup = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $computeResourceGroup = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation"
    $ipAddress = (Invoke-WebRequest -uri "https://api.ipify.org/").Content

    $context = Get-AzContext

    if (!$SkipResourceProvidersCheck) {
        Set-ResourceProviders
    }

    if(!$IsInitialDeployment) {

        $jobTrigger = Get-AzFunctionApp -ResourceGroupName $computeResourceGroup `
                                        -Name "$computeResourceGroup-JobTrigger"       

        Stop-AzFunctionApp -ResourceGroupName $computeResourceGroup -Name $jobTrigger.Name -Force
    }

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

    if(!$IsInitialDeployment -and $ResetGMMType -ne "Skip") {
        Write-Host "`nStopping function apps in resource group $computeResourceGroup"
        Stop-FunctionApps -ResourceGroupName $computeResourceGroup

        . ($scriptsDirectory + '\scripts\Reset-GMM.ps1')

        if($ResetGMMType -eq "Credentials") {
            Reset-GMM-WithCredentials `
                -SolutionAbbreviation $SolutionAbbreviation `
                -EnvironmentAbbreviation $EnvironmentAbbreviation
        } elseif ($ResetGMMType -eq "ServicePrincipal") {
            Reset-GMM-WithServicePrincipal `
                -SolutionAbbreviation $SolutionAbbreviation `
                -EnvironmentAbbreviation $EnvironmentAbbreviation
        }
    }

    if ($StartFunctions) {
        Start-FunctionApps -ResourceGroupName $computeResourceGroup
    }

    if ($response.AppRegistrations.AppsThatNeedAdminConsent.Count -gt 0) {
        Write-Host "`n======================" -ForegroundColor Yellow
        Write-Host "The following applications require admin consent:" -ForegroundColor Yellow
        Write-Host "======================" -ForegroundColor Yellow
        foreach ($app in $response.AppRegistrations.AppsThatNeedAdminConsent) {
            Write-Host $app
        }
        Write-Host "`nPlease visit the Azure portal to grant admin consent for these applications." -ForegroundColor Yellow
    }

    Start-Sleep -Seconds 10

    # open the web app
    $staticWebApp = Get-AzStaticWebApp -Name "$SolutionAbbreviation-ui" -ResourceGroupName $computeResourceGroup
    if ($null -ne $staticWebApp) {
        Write-Host "`nOpening UI in browser, url: https://$($staticWebApp.DefaultHostname)"
        Start-Process "https://$($staticWebApp.DefaultHostname)"
    }
}