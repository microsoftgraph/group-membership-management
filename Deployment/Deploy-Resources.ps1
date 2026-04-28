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

$maxRetriesForDeploymentOperations = 10

$sharedScriptsDirectory = Join-Path $PSScriptRoot "../Scripts"
. (Join-Path $sharedScriptsDirectory 'ReusableModules/Invoke-WithRetry.ps1')
. (Join-Path $sharedScriptsDirectory 'FunctionAppCompat.ps1')

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
    foreach ($namespace in @("Microsoft.ServiceBus", "Microsoft.Insights", "Microsoft.OperationalInsights", "Microsoft.AlertsManagement", "Microsoft.Storage", "Microsoft.AppConfiguration", "Microsoft.Sql", "Microsoft.Web", "Microsoft.DataFactory", "Microsoft.SignalRService", "Microsoft.DevTestLab")) {
        Write-Host "Checking if the resource provider $namespace is registered..."
        $provider = Invoke-WithRetry `
            -Operation { Get-AzResourceProvider -ProviderNamespace $namespace } `
            -OperationName "Get resource provider $namespace" `
            -MaxAttempts 3 -BaseDelaySeconds 2

        if ($provider.Where({ $_.RegistrationState -ne "Registered" }).Count -gt 0) {
            Write-Host "$namespace is not registered. Registering..."
            Invoke-WithRetry `
                -Operation { Register-AzResourceProvider -ProviderNamespace $namespace } `
                -OperationName "Register resource provider $namespace" `
                -MaxAttempts 3 -BaseDelaySeconds 2
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
                $param = $AdditionalParameters.parameters[$_]
                if ($param.Keys -contains "reference") {
                    $commonParametersObject[$_] = @{ reference = $param.reference }
                } else {
                    $commonParametersObject[$_] = @{ value = $param.value }
                }
            }
        }
    }

    # add (or overwrite) from the parameters file
    $TemplateObject.parameters.Keys | ForEach-Object {
        if ($ParameterHashtable.Keys -contains $_) {
            $param = $ParameterHashtable[$_]
            if ($param.Keys -contains "reference") {
                $commonParametersObject[$_] = @{ reference = $param.reference }
            } else {
                $commonParametersObject[$_] = @{ value = $param.value }
            }
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

    Invoke-WithCreateRetry `
        -GetExistingOperation { Get-AzRoleAssignment -ObjectId $ObjectId -Scope $Scope -RoleDefinitionName $RoleDefinitionName } `
        -CreateOperation {
            New-AzRoleAssignment -ObjectId $ObjectId -Scope $Scope -RoleDefinitionName $RoleDefinitionName
            Write-Host "Added role $RoleDefinitionName to $ObjectId on the $KeyVaultName keyvault."
        } `
        -OperationName "Assign $RoleDefinitionName on $KeyVaultName" `
        -MaxAttempts 3 -BaseDelaySeconds 2 `
        -ExistsMessage "Role '$RoleDefinitionName' is already assigned on '$KeyVaultName'. Skipping."
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
    $keyVault = Invoke-WithRetry `
        -Operation {
            Get-AzKeyVault -ResourceGroupName $ResourceGroupName -Name $KeyVaultName
        } `
        -OperationName "Get KeyVault '$KeyVaultName'" `
        -MaxAttempts 3 -BaseDelaySeconds 2

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

    $token = (Invoke-WithRetry `
        -Operation { Get-AzAccessToken -ResourceUrl $Resource } `
        -OperationName "Get Azure access token" `
        -MaxAttempts 3 -BaseDelaySeconds 2).Token

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
    Invoke-WithRetry `
        -Operation {
            Start-ResourceDeployment `
                -SubscriptionId $SubscriptionId `
                -Location $Location `
                -TemplateFilePath $templateFilePath `
                -ParameterHashtable $ParameterHashtable `
                -AdditionalParameters $AdditionalParameters `
                -IsResourceGroupCreation $true
        } `
        -OperationName "Create Resource Groups" `
        -MaxAttempts $maxRetriesForDeploymentOperations `
        -BaseDelaySeconds 2
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
    Invoke-WithRetry `
        -Operation {
            Start-ResourceDeployment `
                -ResourceGroupName $prereqsResourceGroup `
                -SubscriptionId $SubscriptionId `
                -TemplateFilePath $templateFilePath `
                -ParameterHashtable $ParameterHashtable `
                -AdditionalParameters $AdditionalParameters
        } `
        -OperationName "Create prereqs resources" `
        -MaxAttempts $maxRetriesForDeploymentOperations `
        -BaseDelaySeconds 2

    # grant permissions to prereqs key vault
    if ($SetRBACPermissions -eq $true) {
        $currentUser = Invoke-WithRetry `
            -Operation { Get-AzADUser -SignedIn } `
            -OperationName "Get current AD user" `
            -MaxAttempts 3 -BaseDelaySeconds 2
        Set-AdminKeyVaultRoles `
            -UserObjectId $currentUser.Id `
            -KeyVaultName "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation" `
            -ResourceGroupName $prereqsResourceGroup
    }
}

function Set-NetworkingResources {
    param (
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$SubscriptionId,
        [Parameter(Mandatory = $true)]
        [string]$NetworkingTemplateDirectoryPath,
        [Parameter(Mandatory = $true)]
        [Hashtable]$ParameterHashtable,
        [Parameter(Mandatory = $false)]
        [Hashtable]$AdditionalParameters = @{ parameters = @{} }
    )

    $dataResourceGroup = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $networkingResourceGroup = "$SolutionAbbreviation-networking-$EnvironmentAbbreviation"

    # Ensure VM admin secrets exist in the data Key Vault.
    # Generate values in script when missing, then let Bicep set them.
    $vmAdminUsernameSecretName = "vmAdminUsername"
    $vmAdminPasswordSecretName = "vmAdminPassword"

    $networkingAdditionalParameters = @{
        parameters = @{}
    }
    $AdditionalParameters.parameters.Keys | ForEach-Object {
        $networkingAdditionalParameters.parameters[$_] = $AdditionalParameters.parameters[$_]
    }

    Write-Host "Checking VM admin secret presence in Key Vault '$dataResourceGroup'..."
    $usernameExists = Check-IfKeyVaultSecretExists -VaultName $dataResourceGroup -SecretName $vmAdminUsernameSecretName
    $passwordExists = Check-IfKeyVaultSecretExists -VaultName $dataResourceGroup -SecretName $vmAdminPasswordSecretName
    Write-Host ("VM admin secret status: vmAdminUsername={0}, vmAdminPassword={1}" -f $(if ($usernameExists) { 'found' } else { 'missing' }), $(if ($passwordExists) { 'found' } else { 'missing' }))
    $setVmAdminSecrets = (-not $usernameExists) -or (-not $passwordExists)

    if ($setVmAdminSecrets) {
        Write-Host "One or more VM admin secrets are missing in '$dataResourceGroup'. Generating values for deployment..."

        # Generate cryptographically random values for password.
        $upper = -join ((65..90) | Get-Random -Count 4 | ForEach-Object { [char]$_ })
        $lower = -join ((97..122) | Get-Random -Count 4 | ForEach-Object { [char]$_ })
        $digits = -join ((48..57) | Get-Random -Count 4 | ForEach-Object { [char]$_ })
        $special = -join (('!@#$%^&*()-_=+[]{}|;:,.<>?'.ToCharArray()) | Get-Random -Count 4)
        $allPasswordChars = ($upper + $lower + $digits + $special).ToCharArray() | Sort-Object { Get-Random }
        
        $networkingAdditionalParameters.parameters["setVmAdminSecrets"] = @{ value = $setVmAdminSecrets }
        $networkingAdditionalParameters.parameters["vmAdminUsername"] = @{ value = "gmmadmin" }
        $networkingAdditionalParameters.parameters["vmAdminPassword"] = @{ value = -join $allPasswordChars }
    }
    else {
        Write-Host "VM admin secrets already exist in '$dataResourceGroup'. Reusing existing values."
    }

    # Start the jumpbox VM if it exists and is stopped/deallocated.
    # Auto-shutdown may have turned it off; extensions cannot deploy to a non-running VM.
    $vmName = "$SolutionAbbreviation-networking-$EnvironmentAbbreviation-management-vm"
    $vm = Get-AzVM -ResourceGroupName $networkingResourceGroup -Name $vmName -Status -ErrorAction SilentlyContinue
    if ($null -ne $vm) {
        $powerState = ($vm.Statuses | Where-Object { $_.Code -like 'PowerState/*' }).Code
        if ($powerState -in @('PowerState/deallocated', 'PowerState/stopped')) {
            Write-Host "Jumpbox VM '$vmName' is $($powerState -replace 'PowerState/'). Starting it before networking deployment..."
            Start-AzVM -ResourceGroupName $networkingResourceGroup -Name $vmName
            Write-Host "Jumpbox VM '$vmName' started successfully."
        }
        else {
            Write-Host "Jumpbox VM '$vmName' is in state '$($powerState -replace 'PowerState/')'. No action needed."
        }
    }
    else {
        Write-Host "Jumpbox VM '$vmName' not found (first deployment). Skipping VM start."
    }

    # Start the jumpbox VM if it exists and is stopped/deallocated.
    # Auto-shutdown may have turned it off; extensions cannot deploy to a non-running VM.
    $vmName = "$SolutionAbbreviation-networking-$EnvironmentAbbreviation-management-vm"
    $vm = Get-AzVM -ResourceGroupName $networkingResourceGroup -Name $vmName -Status -ErrorAction SilentlyContinue
    if ($null -ne $vm) {
        $powerState = ($vm.Statuses | Where-Object { $_.Code -like 'PowerState/*' }).Code
        if ($powerState -in @('PowerState/deallocated', 'PowerState/stopped')) {
            Write-Host "Jumpbox VM '$vmName' is $($powerState -replace 'PowerState/'). Starting it before networking deployment..."
            Start-AzVM -ResourceGroupName $networkingResourceGroup -Name $vmName
            Write-Host "Jumpbox VM '$vmName' started successfully."
        }
        else {
            Write-Host "Jumpbox VM '$vmName' is in state '$($powerState -replace 'PowerState/')'. No action needed."
        }
    }
    else {
        Write-Host "Jumpbox VM '$vmName' not found (first deployment). Skipping VM start."
    }

    Write-Host "`nCreating networking resources"
    $templateFilePath = "$NetworkingTemplateDirectoryPath/networkingResources.json"
    Invoke-WithRetry `
        -Operation {
            Start-ResourceDeployment `
                -ResourceGroupName $networkingResourceGroup `
                -SubscriptionId $SubscriptionId `
                -TemplateFilePath $templateFilePath `
                -ParameterHashtable $ParameterHashtable `
                -AdditionalParameters $networkingAdditionalParameters
        } `
        -OperationName "Create networking resources" `
        -MaxAttempts $maxRetriesForDeploymentOperations `
        -BaseDelaySeconds 2

    # Clear plaintext VM admin password from memory 
    if ($networkingAdditionalParameters.parameters.ContainsKey("vmAdminPassword")) {
        $networkingAdditionalParameters.parameters["vmAdminPassword"] = $null
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
    Invoke-WithRetry `
        -Operation {
            Start-ResourceDeployment `
                -ResourceGroupName $dataResourceGroup `
                -SubscriptionId $SubscriptionId `
                -TemplateFilePath $templateFilePath `
                -ParameterHashtable $ParameterHashtable `
                -AdditionalParameters $AdditionalParameters
        } `
        -OperationName "Create data resources" `
        -MaxAttempts $maxRetriesForDeploymentOperations `
        -BaseDelaySeconds 2

    # grant permissions to data key vault
    if ($setRBACPermissions -eq $true) {
        $currentUser = Invoke-WithRetry `
            -Operation { Get-AzADUser -SignedIn } `
            -OperationName "Get current AD user" `
            -MaxAttempts 3 -BaseDelaySeconds 2
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
    
    $storageAccountSecretName  = Get-DefaultString -Value $ParameterHashtable.storageAccountSecretName.value -Default "adfStorageAccountName"
    $dataResourceGroup = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $secrets = @("sqlServerMSIConnectionString", $storageAccountSecretName, "sqlServerBasicConnectionString")
    Set-DefaultSecretsIfMissing `
        -KeyVaultName $dataResourceGroup `
        -SecretNames $secrets

    $prereqsResourceGroup = "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation"
    $functionAuthAppClientId = Get-KeyVaultSecretWithFirewallRetry `
                                -VaultName $prereqsResourceGroup `
                                -ResourceGroup $prereqsResourceGroup `
                                -SecretName "functionAuthAppClientId" `
                                -AsPlainText
    
    if ([string]::IsNullOrWhiteSpace($functionAuthAppClientId)) {
        throw "Function Auth App Client Id secret is not set in the Key Vault '$prereqsResourceGroup'. Please set the secret and re-run the deployment."
    }

    $ParameterHashtable["functionAuthAppClientId"] = @{ value = $functionAuthAppClientId }

    Write-Host "`nCreating compute resources"
    $computeResourceGroup = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation"
    $templateFilePath = "$ComputeTemplateDirectoryPath/computeResources.json"
    Invoke-WithRetry `
        -Operation {
            Start-ResourceDeployment `
                -ResourceGroupName $computeResourceGroup `
                -SubscriptionId $SubscriptionId `
                -TemplateFilePath $templateFilePath `
                -ParameterHashtable $ParameterHashtable `
                -AdditionalParameters $AdditionalParameters
        } `
        -OperationName "Create compute resources" `
        -MaxAttempts $maxRetriesForDeploymentOperations `
        -BaseDelaySeconds 2
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

    $prereqsResourceGroup = "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation"
    $functionAuthAppClientId = Get-KeyVaultSecretWithFirewallRetry `
                                -VaultName $prereqsResourceGroup `
                                -ResourceGroup $prereqsResourceGroup `
                                -SecretName "functionAuthAppClientId" `
                                -AsPlainText
    
    if ([string]::IsNullOrWhiteSpace($functionAuthAppClientId)) {
        throw "Function Auth App Client Id secret is not set in the Key Vault '$prereqsResourceGroup'. Please set the secret and re-run the deployment."
    }

    $ParameterHashtable["functionAuthAppClientId"] = @{ value = $functionAuthAppClientId }
    
    # Deploy ADF resources
    Write-Host "`nCreating ADF resources"
    $templateFilePath = "$ADFTemplateDirectoryPath/adfHRResources.json"
    Invoke-WithRetry `
        -Operation {
            Start-ResourceDeployment `
                -ResourceGroupName $dataResourceGroup `
                -SubscriptionId $SubscriptionId `
                -TemplateFilePath $templateFilePath `
                -ParameterHashtable $ParameterHashtable `
                -AdditionalParameters $AdditionalParameters
        } `
        -OperationName "Create ADF resources" `
        -MaxAttempts $maxRetriesForDeploymentOperations `
        -BaseDelaySeconds 2
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
    $commonParametersObject.parameters["networkingResourceGroupName"] = @{"value" = "$SolutionAbbreviation-networking-$EnvironmentAbbreviation" }
    $commonParametersObject.parameters["prereqsKeyVaultName"] = @{"value" = $prereqsResourceGroup }
    $commonParametersObject.parameters["dataKeyVaultName"] = @{"value" = $dataResourceGroup }
    $commonParametersObject.parameters["computeKeyVaultName"] = @{"value" = $computeResourceGroup }
    $commonParametersObject.parameters["appConfigurationName"] = @{"value" = "$SolutionAbbreviation-appConfig-$EnvironmentAbbreviation" }
    $commonParametersObject.parameters["apiServiceBaseUri"] = @{"value" = "https://$SolutionAbbreviation-compute-$EnvironmentAbbreviation-webapi.azurewebsites.net" }

    return $commonParametersObject
}

function Set-FunctionAuthenticationAllowedIdentities {
    param (
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$SubscriptionId,
        [Parameter(Mandatory = $false)]
        [bool]$SkipAzureDataFactoryDeployment = $false,
        [Parameter(Mandatory = $false)]
        [string[]]$AdditionalAdfFunctionAppNames = @(),
        [Parameter(Mandatory = $false)]
        [string[]]$AdditionalWebApiFunctionAppNames = @()
    )

    Write-Host "`n" -NoNewline
    Write-Host ("=" * 60) -ForegroundColor Cyan
    Write-Host "  Setting Allowed Identities for Function Authentication" -ForegroundColor Cyan
    Write-Host ("=" * 60) -ForegroundColor Cyan

    $dataResourceGroup = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $computeResourceGroup = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation"

    # Get ADF Managed Identity Principal ID (if ADF is deployed)
    $adfMSIPrincipalId = $null
    if ($SkipAzureDataFactoryDeployment -eq $false) {
        Write-Host "  Retrieving ADF Managed Identity..." -ForegroundColor Yellow
        $adfResourceName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation-adf"
        $adfResource = Invoke-WithRetry `
            -Operation { Get-AzResource -ResourceGroupName $dataResourceGroup -ResourceType "Microsoft.DataFactory/factories" -Name $adfResourceName -ErrorAction SilentlyContinue } `
            -OperationName "Get ADF resource" `
            -MaxAttempts 3 -BaseDelaySeconds 2
        if ($null -ne $adfResource -and $null -ne $adfResource.Identity) {
            $adfMSIPrincipalId = $adfResource.Identity.PrincipalId
            if (-not [string]::IsNullOrWhiteSpace($adfMSIPrincipalId)) {
                Write-Host "  ✓ ADF Managed Identity: $adfMSIPrincipalId" -ForegroundColor Green
            }
        }
        else {
            Write-Host "  ⚠ ADF resource not found or has no managed identity" -ForegroundColor Yellow
        }
    }
    else {
        Write-Host "  ⏭ Skipping ADF identity (ADF deployment was skipped)" -ForegroundColor Yellow
    }

    # Get WebAPI Managed Identity Principal ID
    $webApiMSIPrincipalId = $null
    $webApiResourceName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-webapi"
    Write-Host "  Retrieving WebAPI Managed Identity..." -ForegroundColor Yellow
    $webApiResource = Invoke-WithRetry `
        -Operation { Get-AzWebApp -ResourceGroupName $computeResourceGroup -Name $webApiResourceName -ErrorAction SilentlyContinue } `
        -OperationName "Get WebAPI resource" `
        -MaxAttempts 3 -BaseDelaySeconds 2
    if ($null -ne $webApiResource -and $null -ne $webApiResource.Identity) {
        $webApiMSIPrincipalId = $webApiResource.Identity.PrincipalId
        if (-not [string]::IsNullOrWhiteSpace($webApiMSIPrincipalId)) {
            Write-Host "  ✓ WebAPI Managed Identity: $webApiMSIPrincipalId" -ForegroundColor Green
        }
    }
    else {
        Write-Host "  ⚠ WebAPI resource not found or has no managed identity" -ForegroundColor Yellow
    }

    # Define function apps that need ADF access
    $adfFunctionAppNames = @("AzureUserReader", "NonProdService", "SqlDataChecker")
    $adfFunctionAppNames += $AdditionalAdfFunctionAppNames

    # Define function apps that need WebAPI access
    $webApiFunctionAppNames = @("JobScheduler")
    $webApiFunctionAppNames += $AdditionalWebApiFunctionAppNames

    

    # Update function apps that need ADF access
    if ([string]::IsNullOrWhiteSpace($adfMSIPrincipalId) -eq $false) {
        Write-Host "`n  Updating function apps for ADF access..." -ForegroundColor Yellow
        Update-FunctionAppAuthSettings -FunctionAppNames $adfFunctionAppNames `
            -AllowedPrincipalIds @($adfMSIPrincipalId) `
            -SolutionAbbreviation $SolutionAbbreviation `
            -EnvironmentAbbreviation $EnvironmentAbbreviation `
            -SubscriptionId $SubscriptionId
    }
    else {
        Write-Host "`n  ⚠ No ADF identities found. Skipping ADF function app updates." -ForegroundColor Yellow
    }

    # Update function apps that need WebAPI access
    if ([string]::IsNullOrWhiteSpace($webApiMSIPrincipalId) -eq $false) {
        Write-Host "`n  Updating function apps for WebAPI access..." -ForegroundColor Yellow
        Update-FunctionAppAuthSettings -FunctionAppNames $webApiFunctionAppNames `
            -AllowedPrincipalIds @($webApiMSIPrincipalId) `
            -SolutionAbbreviation $SolutionAbbreviation `
            -EnvironmentAbbreviation $EnvironmentAbbreviation `
            -SubscriptionId $SubscriptionId
    }
    else {
        Write-Host "`n  ⚠ No WebAPI identities found. Skipping WebAPI function app updates." -ForegroundColor Yellow
    }

    Write-Host "`n" -NoNewline
    Write-Host ("=" * 60) -ForegroundColor Green
    Write-Host "  ✓ Function Authentication Identities Updated" -ForegroundColor Green
    Write-Host ("=" * 60) -ForegroundColor Green
    Write-Host ""
}

function Update-FunctionAppAuthSettings {
    param (
        [Parameter(Mandatory = $true)]
        [string[]]$FunctionAppNames,
        [Parameter(Mandatory = $true)]
        [string[]]$AllowedPrincipalIds,
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$SubscriptionId
    )

    $computeResourceGroup = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation"
    $functionApps = @()
    foreach ($shortName in $FunctionAppNames) {
        $fullFunctionName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-$shortName"
        $app = Invoke-WithRetry `
            -Operation { Get-FunctionAppCompat -ResourceGroupName $computeResourceGroup -Name $fullFunctionName -ErrorAction SilentlyContinue } `
            -OperationName "Get function app '$fullFunctionName'" `
            -MaxAttempts 3 -BaseDelaySeconds 2
        if ($null -ne $app) {
            $functionApps += $app
            Write-Host "  ✓ Found function app: $fullFunctionName" -ForegroundColor Green
        }
        else {
            Write-Host "  ⚠ Function app not found: $fullFunctionName" -ForegroundColor Yellow
        }
    }

    if ($null -eq $functionApps -or $functionApps.Count -eq 0) {
        Write-Host "  ⚠ No Function Apps found." -ForegroundColor Yellow
        return
    }

    $token = Get-BearerToken
    $headers = @{
        Authorization  = "Bearer $token"
        'Content-Type' = 'application/json'
    }

    $functionIndex = 0
    $totalFunctions = $functionApps.Count

    foreach ($functionApp in $functionApps) {
        $functionIndex++
        $functionAppName = $functionApp.Name

        Write-Host "  [$functionIndex/$totalFunctions] Updating: $functionAppName" -ForegroundColor Gray

        try {
            # Get current auth settings
            $getUri = "https://management.azure.com/subscriptions/$SubscriptionId/resourceGroups/$computeResourceGroup/providers/Microsoft.Web/sites/$functionAppName/config/authsettingsV2?api-version=2022-09-01"
            $currentAuthSettings = Invoke-WithRetry `
                -Operation { Invoke-RestMethod -Uri $getUri -Method Get -Headers $headers } `
                -OperationName "Get auth settings for $functionAppName" `
                -MaxAttempts 3 -BaseDelaySeconds 2

            # Get existing allowed principals and merge with new ones
            $existingIdentities = @()
            if ($null -ne $currentAuthSettings.properties.identityProviders.azureActiveDirectory.validation.defaultAuthorizationPolicy.allowedPrincipals.identities) {
                $existingIdentities = @($currentAuthSettings.properties.identityProviders.azureActiveDirectory.validation.defaultAuthorizationPolicy.allowedPrincipals.identities)
            }

            # Combine existing and new identities, then deduplicate
            $combinedIdentities = ($existingIdentities + $AllowedPrincipalIds) | Select-Object -Unique

            # Add allowedPrincipals.identities to the validation section
            if ($null -eq $currentAuthSettings.properties.identityProviders.azureActiveDirectory.validation.defaultAuthorizationPolicy) {
                $currentAuthSettings.properties.identityProviders.azureActiveDirectory.validation | Add-Member -NotePropertyName "defaultAuthorizationPolicy" -NotePropertyValue @{} -Force
            }

            $currentAuthSettings.properties.identityProviders.azureActiveDirectory.validation.defaultAuthorizationPolicy = @{
                allowedPrincipals = @{
                    identities = $combinedIdentities
                }
            }

            # Update auth settings
            $putUri = "https://management.azure.com/subscriptions/$SubscriptionId/resourceGroups/$computeResourceGroup/providers/Microsoft.Web/sites/$functionAppName/config/authsettingsV2?api-version=2022-09-01"
            $body = $currentAuthSettings | ConvertTo-Json -Depth 20

            $null = Invoke-WithRetry `
                -Operation { Invoke-RestMethod -Uri $putUri -Method Put -Headers $Headers -Body $body } `
                -OperationName "Update auth settings for $functionAppName" `
                -MaxAttempts 3 -BaseDelaySeconds 2

            Write-Host "    ✓ Successfully updated $functionAppName" -ForegroundColor Green
        }
        catch {
            Write-Host "    ✗ Failed to update $($functionAppName): $($_.Exception.Message)" -ForegroundColor Red
        }
    }

    # Clear sensitive token from memory
    $token = $null
    $headers = $null
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
    $createResourceGroups = Get-Default -Value $ParameterHashtable['createResourceGroups'].value -Default $false
    $skipAzureDataFactoryDeployment = Get-Default -Value $ParameterHashtable['skipAzureDataFactoryDeployment'].value -Default $false
    $ipRangesToWhiteList = Get-Default -Value $ParameterHashtable['IpRangesToWhiteList'].value -Default @()

    # strings
    $graphAppCertificateName        = Get-DefaultString -Value $ParameterHashtable['graphAppCertificateName'].value        -Default 'not-set'
    $teamsChannelAppCertificateName = Get-DefaultString -Value $ParameterHashtable['teamsChannelAppCertificateName'].value -Default 'not-set'
    $directoryTenantId              = Get-DefaultString -Value $ParameterHashtable['directoryTenantId'].value -Default $ParameterHashtable.tenantId.value

    $hostIpAddress = (Invoke-WithRetry `
        -Operation { Invoke-WebRequest -uri "https://api.ipify.org/" } `
        -OperationName "Get host IP address" `
        -MaxAttempts 3 -BaseDelaySeconds 2).Content
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
        -SetRBACPermissions             $setRBACPermissions

    Start-Sleep -Seconds 10

    Set-KeyVaultFirewallRules `
        -ResourceGroups @($prereqsResourceGroup) `
        -ipAddresses $ipAddressesToWhiteList `
        -ScriptsDirectory $ScriptsDirectory `
        -Region $Location

    # determine networking deployment behavior
    $skipNetworkingDeployment = Get-Default -Value $ParameterHashtable['skipNetworkingDeployment'].value -Default $false

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
        -SetRBACPermissions         $setRBACPermissions

    Start-Sleep -Seconds 10

    Set-KeyVaultFirewallRules `
        -ResourceGroups @($dataResourceGroup) `
        -ipAddresses $ipAddressesToWhiteList `
        -ScriptsDirectory $ScriptsDirectory `
        -Region $Location

    # deploy networking resources after data resources so networking can provision
    # private endpoints that target data resources (SQL primary/replica, data KV).
    if ($skipNetworkingDeployment -eq $false) {
        Set-NetworkingResources `
            -SolutionAbbreviation           $SolutionAbbreviation `
            -EnvironmentAbbreviation        $EnvironmentAbbreviation `
            -SubscriptionId                 $SubscriptionId `
            -NetworkingTemplateDirectoryPath $TemplateFilesDirectory `
            -ParameterHashtable             $ParameterHashtable `
            -AdditionalParameters           $commonParametersObject

        Start-Sleep -Seconds 10
    }
    else {
        Write-Host "`nSkipping networking deployment as per configuration [skipNetworkingDeployment = $skipNetworkingDeployment]." -ForegroundColor Yellow
    }
    
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

    Set-FunctionAuthenticationAllowedIdentities `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -SubscriptionId $SubscriptionId `
        -SkipAzureDataFactoryDeployment $skipAzureDataFactoryDeployment

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
    $ipAddress = (Invoke-WithRetry `
        -Operation { Invoke-WebRequest -uri "https://api.ipify.org/" } `
        -OperationName "Get host IP address" `
        -MaxAttempts 3 -BaseDelaySeconds 2).Content
    $sqlIPRuleName = "DeploymentScript_Client_IP_Address-$ipAddress"
    Invoke-WithCreateRetry `
        -GetExistingOperation {
            Get-AzSqlServerFirewallRule -FirewallRuleName $sqlIPRuleName -ResourceGroupName $dataResourceGroupName -ServerName $sqlServerName -ErrorAction SilentlyContinue
        } `
        -CreateOperation {
            New-AzSqlServerFirewallRule -ResourceGroupName $dataResourceGroupName -ServerName $sqlServerName -FirewallRuleName $sqlIPRuleName -StartIpAddress $ipAddress -EndIpAddress $ipAddress
            Write-Host "Added firewall rule for SQL Server"
        } `
        -OperationName "Create SQL firewall rule" `
        -MaxAttempts 3 -BaseDelaySeconds 2 `
        -ExistsMessage "SQL firewall rule '$sqlIPRuleName' already exists on '$sqlServerName'. Skipping."
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
    $functionApps = Invoke-WithRetry `
        -Operation { Get-AzResource -ResourceGroupName $computeResourceGroup -ResourceType "Microsoft.Web/sites" } `
        -OperationName "Get function apps for SQL permissions" `
        -MaxAttempts 3 -BaseDelaySeconds 2
    
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
    $dataFactory = Invoke-WithRetry `
        -Operation { Get-AzDataFactoryV2 -ResourceGroupName $dataResourceGroup -Name $dataFactoryName -ErrorAction SilentlyContinue } `
        -OperationName "Get Data Factory" `
        -MaxAttempts 3 -BaseDelaySeconds 2
    $functionAppsADF = $functionApps | Where-Object { $_.Name -match "-webapi" -or $_.Name -match "-SqlMembershipObtainer" -or $_.Name -match "-SqlDataChecker" }

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
        [boolean] $SkipPrivilegedDirectoryActions = $false,
        [Parameter(Mandatory = $false)]
        [bool] $SkipNetworkingDeployment = $false,
        [Parameter(Mandatory = $false)]
        [string] $BastionVnetAddressPrefix = '10.0.0.0/24'
    )

    # grant permissions to resources
    Write-Host "`nGranting permissions to resources"

    . ($ScriptsDirectory + '/Set-PostDeploymentRoles.ps1')
    Set-PostDeploymentRoles `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -TenantId $TenantId `
        -SetUserAssignedManagedIdentityPermissions $SetUserAssignedManagedIdentityPermissions `
        -SkipPrivilegedDirectoryActions $SkipPrivilegedDirectoryActions `
        -SkipNetworkingDeployment $SkipNetworkingDeployment `
        -BastionVnetAddressPrefix $BastionVnetAddressPrefix

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

    $functionApps = Invoke-WithRetry `
        -Operation { Get-FunctionAppCompat -ResourceGroupName $ComputeResourceGroup } `
        -OperationName "Get function apps for code deploy" `
        -MaxAttempts 3 -BaseDelaySeconds 2
    foreach ($functionApp in $functionApps) {

        Write-Host "Publishing code for function app $($functionApp.Name)"

        $functionName = $functionApp.Name.Split("-")[3]
        $packageFile = "$FunctionsPackagesDirectory/$functionName.zip"

        if (-not (Test-Path $packageFile)) {
            Write-Host "Package file not found: $packageFile"
            continue
        }

        Invoke-WithRetry `
            -Operation {
                Publish-AzWebApp -ResourceGroupName $ComputeResourceGroup -Name $functionApp.Name -ArchivePath $packageFile -Force
            } `
            -OperationName "Deploying code for $($functionApp.Name)" `
            -MaxAttempts $maxRetriesForDeploymentOperations `
            -BaseDelaySeconds 2

        Write-Host "Successfully published code for function app $($functionApp.Name)`n" -ForegroundColor Green

        if ($functionApp.Kind -eq "functionapp") {
            Write-Host "Function app $($functionApp.Name) is on Comsumption. Setting functionAppScaleLimit = 1..."
            Invoke-WithRetry `
                -Operation {
                    Set-AzResource -ResourceGroupName $ComputeResourceGroup `
                        -ResourceType "Microsoft.Web/sites" `
                        -ResourceName "$($functionApp.Name)/config/web" `
                        -ApiVersion "2022-03-01" `
                        -Properties @{ functionAppScaleLimit = 1 } `
                        -Force
                } `
                -OperationName "Set scale limit for $($functionApp.Name)" `
                -MaxAttempts 3 -BaseDelaySeconds 2
            Write-Host "Successfully set functionAppScaleLimit for $($functionApp.Name)`n" -ForegroundColor Green
        }
        
    }

    # publish web api code
    Write-Host "`nPublishing code for webapi app $ComputeResourceGroup-webapi"
    $webApi = Invoke-WithRetry `
        -Operation { Get-AzWebApp -ResourceGroupName $ComputeResourceGroup -Name "$ComputeResourceGroup-webapi" } `
        -OperationName "Get WebAPI app for code deploy" `
        -MaxAttempts 3 -BaseDelaySeconds 2
    $webApiName = $webApi.Name.Split("-")[3]

    Invoke-WithRetry `
        -Operation {
            Publish-AzWebApp -ResourceGroupName $ComputeResourceGroup -Name $webApi.Name -ArchivePath "$WebApiPackagesDirectory/$webApiName.zip" -Force
        } `
        -OperationName "Deploying code for $($webApi.Name)" `
        -MaxAttempts $maxRetriesForDeploymentOperations `
        -BaseDelaySeconds 2
    
    Write-Host "Successfully published code for web api app $($webApi.Name)`n" -ForegroundColor Green
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
        $keyVaults = Invoke-WithRetry `
            -Operation { Get-AzKeyVault -ResourceGroupName $resourceGroup } `
            -OperationName "Get key vaults in $resourceGroup" `
            -MaxAttempts 3 -BaseDelaySeconds 2
        foreach ($keyVault in $keyVaults) {
            $keyVaultName = $keyVault.VaultName
            $keyVaultResourceGroup = $keyVault.ResourceGroupName

            Write-Host "Fetching existing rules for $keyVaultName"
            $detailedKeyVault = Invoke-WithRetry `
                -Operation { Get-AzKeyVault -Name $keyVaultName -ResourceGroupName $keyVaultResourceGroup } `
                -OperationName "Get key vault details '$keyVaultName'" `
                -MaxAttempts 3 -BaseDelaySeconds 2
            $existingRules = $detailedKeyVault.NetworkAcls.IpAddressRanges

            # Extract current IP rules
            $existingIps = $existingRules.IpRules | ForEach-Object { $_.IpAddress }

            # Combine existing and new, remove duplicates
            $combinedIpRules = ($existingIps + $newIpRules) | Sort-Object -Unique

            Write-Host "Applying updated firewall rules to $keyVaultName"

            Invoke-WithRetry `
                -Operation {
                    Update-AzKeyVaultNetworkRuleSet `
                        -VaultName $keyVaultName `
                        -ResourceGroupName $keyVaultResourceGroup `
                        -Bypass AzureServices `
                        -DefaultAction Deny `
                        -IpAddressRange $combinedIpRules
                } `
                -OperationName "Update firewall rules for '$keyVaultName'" `
                -MaxAttempts 3 -BaseDelaySeconds 2
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

    $functionApps = Invoke-WithRetry `
        -Operation { Get-FunctionAppCompat -ResourceGroupName $ResourceGroupName } `
        -OperationName "Get function apps to stop" `
        -MaxAttempts 3 -BaseDelaySeconds 2
    foreach ($functionApp in $functionApps) {
        Write-Host "Stopping function app $($functionApp.Name)"
        Invoke-WithRetry `
            -Operation { Stop-FunctionAppCompat -ResourceGroupName $ResourceGroupName -Name $functionApp.Name } `
            -OperationName "Stop $($functionApp.Name)" `
            -MaxAttempts 3 -BaseDelaySeconds 2
    }
}

function Start-FunctionApps {
    param (
        [Parameter(Mandatory = $true)]
        [string]$ResourceGroupName,
        [Parameter(Mandatory = $false)]
        [bool]$SkipJobTrigger = $false,
        [Parameter(Mandatory = $false)]
        [int]$JobTriggerDelaySeconds = 60
    )

    $rgObject = Get-AzResourceGroup -Name $ResourceGroupName -ErrorAction SilentlyContinue
    if ($null -eq $rgObject) {
        return
    }

    Write-Host "`nStarting function apps"

    $jobTriggerApp = $null

    $functionApps = Invoke-WithRetry `
        -Operation { Get-FunctionAppCompat -ResourceGroupName $ResourceGroupName } `
        -OperationName "Get function apps to start" `
        -MaxAttempts 3 -BaseDelaySeconds 2
        
    foreach ($functionApp in $functionApps) {
        if ($functionApp.Name -match "JobTrigger") {
            $jobTriggerApp = $functionApp
            Write-Host "Skipping $($functionApp.Name) (will start last)"
            continue
        }
        Write-Host "Starting function app $($functionApp.Name)"
        Invoke-WithRetry `
            -Operation { Start-FunctionAppCompat -ResourceGroupName $ResourceGroupName -Name $functionApp.Name } `
            -OperationName "Start $($functionApp.Name)" `
            -MaxAttempts 3 -BaseDelaySeconds 2
    }

    if ($null -ne $jobTriggerApp -and -not $SkipJobTrigger) {
        Write-Host "`nWaiting $JobTriggerDelaySeconds seconds before starting JobTrigger..."
        Start-Sleep -Seconds $JobTriggerDelaySeconds
        Write-Host "Starting $($jobTriggerApp.Name)"
        Start-FunctionAppCompat -ResourceGroupName $ResourceGroupName -Name $jobTriggerApp.Name
    }
}

function Update-AppSettingsVersion {
    param (
        [Parameter(Mandatory = $true)]
        [string]$ComputeResourceGroupName
    )

    Write-Host "`nChecking function app settings"
    $functionApps = Invoke-WithRetry `
        -Operation { Get-FunctionAppCompat -ResourceGroupName $ComputeResourceGroupName } `
        -OperationName "Get function apps for settings update" `
        -MaxAttempts 3 -BaseDelaySeconds 2
    foreach ($function in $functionApps) {

        $settings = Invoke-WithRetry `
            -Operation { Get-FunctionAppSettingCompat -ResourceGroupName $ComputeResourceGroupName -Name $function.Name } `
            -OperationName "Get settings for $($function.Name)" `
            -MaxAttempts 3 -BaseDelaySeconds 2
        foreach ($key in $settings.Keys) {
            if (-not ($settings[$key].Contains("Microsoft.KeyVault"))) {
                continue
            }

            $kvReference = Get-KeyVaultReference -KeyVaultReference $settings[$key]
            $latestSecretVersion = Get-KeyVaultSecretWithFirewallRetry -ResourceGroup $kvReference.KeyVaultName -VaultName $kvReference.KeyVaultName -SecretName $kvReference.SecretName

            if ($latestSecretVersion.Version -ne $kvReference.Version) {
                Write-Host "Updating $($function.Name) -> $($kvReference.SecretName) to $($latestSecretVersion.Version)"
                $updatedVersion = $settings[$key] -replace $kvReference.Version, $latestSecretVersion.Version
                $updatedSettings = Invoke-WithRetry `
                    -Operation { Update-FunctionAppSettingCompat -Name $function.Name -ResourceGroupName $ComputeResourceGroupName -AppSetting @{$key = $updatedVersion } } `
                    -OperationName "Update setting for $($function.Name)" `
                    -MaxAttempts 3 -BaseDelaySeconds 2
            }
        }
    }

    Write-Host "`nChecking web app settings"
    $webApps = Invoke-WithRetry `
        -Operation { Get-AzWebApp -ResourceGroupName $ComputeResourceGroupName | Where-Object { $_.Kind -eq "app" } } `
        -OperationName "Get web apps for settings update" `
        -MaxAttempts 3 -BaseDelaySeconds 2
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

            Invoke-WithRetry `
                -Operation { Set-AzWebApp -ResourceGroupName $ComputeResourceGroupName -Name $webApp.Name -AppSettings $updatedSettings } `
                -OperationName "Update web app settings for $($webApp.Name)" `
                -MaxAttempts 3 -BaseDelaySeconds 2
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
        [string]$DirectoryTenantId,
        [Parameter(Mandatory = $false)]
        [string]$KeyVaultTenantId,
        [Parameter(Mandatory = $false)]
        [string]$SubscriptionName,
        [Parameter(Mandatory = $false)]
        [boolean]$SaveToKeyVault = $false,
        [Parameter(Mandatory = $false)]
        [switch]$SkipFunctionAuthApp
    )

    Write-Host "`n📝 Creating app registrations programmatically...`n" -ForegroundColor Cyan

    . ($ScriptsDirectory + '/ApplicationSetupScripts/Set-UIAzureADApplication.ps1')
    $uiInformation = Set-UIAzureADApplication `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -AppTenantId $DirectoryTenantId `
        -KeyVaultTenantId $KeyVaultTenantId `
        -SubscriptionName $SubscriptionName `
        -SaveToKeyVault $SaveToKeyVault `
        -SkipIfApplicationExists $false `
        -Clean $false

    . ($ScriptsDirectory + '/ApplicationSetupScripts/Set-WebApiAzureADApplication.ps1')
    $apiInformation = Set-WebApiAzureADApplication `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -AppTenantId $DirectoryTenantId `
        -KeyVaultTenantId $KeyVaultTenantId `
        -SubscriptionName $SubscriptionName `
        -SaveToKeyVault $SaveToKeyVault `
        -SkipIfApplicationExists $false `
        -Clean $false

    . ($ScriptsDirectory + '/ApplicationSetupScripts/Set-GraphCredentialsAzureADApplication.ps1')
    $graphInformation = Set-GraphCredentialsAzureADApplication `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -AppTenantId $DirectoryTenantId `
        -KeyVaultTenantId $KeyVaultTenantId `
        -SubscriptionName $SubscriptionName `
        -SaveToKeyVault $SaveToKeyVault `
        -SkipIfApplicationExists $false `
        -Clean $false

    . ($ScriptsDirectory + '/ApplicationSetupScripts/Set-TeamsChannelAzureADApplication.ps1')
    $teamsChannelInformation = Set-TeamsChannelAzureADApplication `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -AppTenantId $DirectoryTenantId `
        -KeyVaultTenantId $KeyVaultTenantId `
        -SubscriptionName $SubscriptionName `
        -SaveToKeyVault $SaveToKeyVault `
        -SkipIfApplicationExists $false `
        -Clean $false

    if (-not $SkipFunctionAuthApp.IsPresent) {
        . ($ScriptsDirectory + '/ApplicationSetupScripts/Set-FunctionAuthApplication.ps1')
        $functionAuthInformation = Set-FunctionAuthApplication `
            -SolutionAbbreviation $SolutionAbbreviation `
            -EnvironmentAbbreviation $EnvironmentAbbreviation `
            -AppTenantId $DirectoryTenantId `
            -KeyVaultTenantId $KeyVaultTenantId `
            -SubscriptionName $SubscriptionName `
            -SaveToKeyVault $SaveToKeyVault `
            -SkipIfApplicationExists $false `
            -Clean $false
    }
    else {
        Write-Host "Skipping FunctionAuth app registration as per configuration." -ForegroundColor Yellow
    }

    # determine which apps need admin consent
    $appInformationObjects = @(
        $uiInformation,
        $apiInformation,
        $graphInformation,
        $teamsChannelInformation,
        $functionAuthInformation
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
        FunctionAuthAppId = $functionAuthInformation.ApplicationId
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
        [string] $TeamsChannelAppCertificateName
    )

    Write-Host "`n🔐 Saving App Registration Secrets to Key Vault" -ForegroundColor Cyan
    Write-Host "═══════════════════════════════════════════════════════════════════════════" -ForegroundColor Cyan

    $applicationSetupScriptsDirectory = Join-Path $ScriptsDirectory "ApplicationSetupScripts"

    # Retrieve Application IDs
    Write-Host "`n📋 Retrieving App Registration IDs..." -ForegroundColor Yellow
    
    $uiAppId = (Invoke-WithRetry `
        -Operation { Get-MgApplication -Filter "displayName eq '$SolutionAbbreviation-ui-$EnvironmentAbbreviation'" } `
        -OperationName "Get UI app registration" `
        -MaxAttempts 3 -BaseDelaySeconds 2).AppId
    if (-not $uiAppId) {
        Write-Error "UI Application '$SolutionAbbreviation-ui-$EnvironmentAbbreviation' not found"
        return
    }
    Write-Host "  ✓ UI App ID: $uiAppId" -ForegroundColor Gray

    $webApiAppId = (Invoke-WithRetry `
        -Operation { Get-MgApplication -Filter "displayName eq '$SolutionAbbreviation-webapi-$EnvironmentAbbreviation'" } `
        -OperationName "Get WebAPI app registration" `
        -MaxAttempts 3 -BaseDelaySeconds 2).AppId
    if (-not $webApiAppId) {
        Write-Error "WebAPI Application '$SolutionAbbreviation-webapi-$EnvironmentAbbreviation' not found"
        return
    }
    Write-Host "  ✓ WebAPI App ID: $webApiAppId" -ForegroundColor Gray

    $graphAppId = (Invoke-WithRetry `
        -Operation { Get-MgApplication -Filter "displayName eq '$SolutionAbbreviation-Graph-$EnvironmentAbbreviation'" } `
        -OperationName "Get Graph app registration" `
        -MaxAttempts 3 -BaseDelaySeconds 2).AppId
    if (-not $graphAppId) {
        Write-Error "Graph Application '$SolutionAbbreviation-Graph-$EnvironmentAbbreviation' not found"
        return
    }
    Write-Host "  ✓ Graph App ID: $graphAppId" -ForegroundColor Gray

    $teamsChannelAppId = (Invoke-WithRetry `
        -Operation { Get-MgApplication -Filter "displayName eq '$SolutionAbbreviation-TeamsChannel-$EnvironmentAbbreviation'" } `
        -OperationName "Get Teams Channel app registration" `
        -MaxAttempts 3 -BaseDelaySeconds 2).AppId
    if (-not $teamsChannelAppId) {
        Write-Error "Teams Channel Application '$SolutionAbbreviation-TeamsChannel-$EnvironmentAbbreviation' not found"
        return
    }
    Write-Host "  ✓ Teams Channel App ID: $teamsChannelAppId" -ForegroundColor Gray

    $functionAuthAppId = (Invoke-WithRetry `
        -Operation { Get-MgApplication -Filter "displayName eq '$SolutionAbbreviation-FunctionAuth-$EnvironmentAbbreviation'" } `
        -OperationName "Get FunctionAuth app registration" `
        -MaxAttempts 3 -BaseDelaySeconds 2).AppId
    if (-not $functionAuthAppId) {
        Write-Error "FunctionAuth Application '$SolutionAbbreviation-FunctionAuth-$EnvironmentAbbreviation' not found"
        return
    }
    Write-Host "  ✓ FunctionAuth App ID: $functionAuthAppId" -ForegroundColor Gray

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

    # FunctionAuth Application Secrets
    Write-Host "`n📝 Saving FunctionAuth Application secrets..." -ForegroundColor Yellow

    $functionAuthScriptPath = Join-Path $applicationSetupScriptsDirectory "Set-FunctionAuthApplication.ps1"
    . $functionAuthScriptPath

    Set-FunctionAuthKeyVaultSecrets `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -AppTenantId $AppTenantId `
        -FunctionAuthAppClientId $functionAuthAppId

    Write-Host "✅ FunctionAuth Application secrets saved" -ForegroundColor Green

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
    Write-Host "   5. FunctionAuth App        ($SolutionAbbreviation-FunctionAuth-$EnvironmentAbbreviation)" -ForegroundColor White

    Write-Host "`n📖 Manual Setup Documentation:" -ForegroundColor Cyan
    Write-Host "   Please refer to the following documentation for manual setup steps:" -ForegroundColor White
    Write-Host "   - $ScriptsDirectory/ApplicationSetupScripts/Manual Setup Documentation/UI-Application-Creation-Instructions.md" -ForegroundColor Gray
    Write-Host "   - $ScriptsDirectory/ApplicationSetupScripts/Manual Setup Documentation/WebAPI-Application-Creation-Instructions.md" -ForegroundColor Gray
    Write-Host "   - $ScriptsDirectory/ApplicationSetupScripts/Manual Setup Documentation/GraphCredentials-Application-Creation-Instructions.md" -ForegroundColor Gray
    Write-Host "   - $ScriptsDirectory/ApplicationSetupScripts/Manual Setup Documentation/TeamsChannel-Application-Creation-Instructions.md" -ForegroundColor Gray
    Write-Host "   - $ScriptsDirectory/ApplicationSetupScripts/Manual Setup Documentation/FunctionAuth-Application-Creation-Instructions.md" -ForegroundColor Gray

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

    Write-Host "   # 5. FunctionAuth Application" -ForegroundColor Green
    Write-Host "   . `"$ScriptsDirectory/ApplicationSetupScripts/Set-FunctionAuthApplication.ps1`"" -ForegroundColor Gray
    Write-Host "   Set-FunctionAuthApplication ``" -ForegroundColor Gray
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
    . ($ScriptsDirectory + '/ApplicationSetupScripts/Set-FunctionAuthApplication.ps1')

    # Validate each application
    $uiValid = Test-UIApplication -SolutionAbbreviation $SolutionAbbreviation -EnvironmentAbbreviation $EnvironmentAbbreviation
    $webApiValid = Test-WebApiApplication -SolutionAbbreviation $SolutionAbbreviation -EnvironmentAbbreviation $EnvironmentAbbreviation
    $graphValid = Test-GraphCredentialsApplication -SolutionAbbreviation $SolutionAbbreviation -EnvironmentAbbreviation $EnvironmentAbbreviation
    $teamsChannelValid = Test-TeamsChannelApplication -SolutionAbbreviation $SolutionAbbreviation -EnvironmentAbbreviation $EnvironmentAbbreviation
    $functionAuthValid = Test-FunctionAuthApplication -SolutionAbbreviation $SolutionAbbreviation -EnvironmentAbbreviation $EnvironmentAbbreviation

    Write-Host "`n═══════════════════════════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "📊 Validation Summary:" -ForegroundColor Cyan
    Write-Host "   UI Application:           $(if ($uiValid) { '✅ PASS' } else { '❌ FAIL' })" -ForegroundColor $(if ($uiValid) { 'Green' } else { 'Red' })
    Write-Host "   WebAPI Application:       $(if ($webApiValid) { '✅ PASS' } else { '❌ FAIL' })" -ForegroundColor $(if ($webApiValid) { 'Green' } else { 'Red' })
    Write-Host "   Graph Application:        $(if ($graphValid) { '✅ PASS' } else { '❌ FAIL' })" -ForegroundColor $(if ($graphValid) { 'Green' } else { 'Red' })
    Write-Host "   Teams Channel Application: $(if ($teamsChannelValid) { '✅ PASS' } else { '❌ FAIL' })" -ForegroundColor $(if ($teamsChannelValid) { 'Green' } else { 'Red' })
    Write-Host "   FunctionAuth Application: $(if ($functionAuthValid) { '✅ PASS' } else { '❌ FAIL' })" -ForegroundColor $(if ($functionAuthValid) { 'Green' } else { 'Red' })
    Write-Host "═══════════════════════════════════════════════════════════════════════════`n" -ForegroundColor Cyan

    if (-not ($uiValid -and $webApiValid -and $graphValid -and $teamsChannelValid -and $functionAuthValid)) {
        Write-Host "❌ One or more applications failed validation. Please review the errors above and fix the issues." -ForegroundColor Red
        Write-Host "   You can re-run the validation by calling the Test-*Application functions individually.`n" -ForegroundColor Yellow
        throw "App registration validation failed. Please fix the issues and try again."
    }

    Write-Host "✅ All app registrations validated successfully!`n" -ForegroundColor Green

    # Retrieve application details to check for admin consent requirements
    $uiApp = Invoke-WithRetry `
        -Operation { Get-MgApplication -Filter "displayName eq '$SolutionAbbreviation-ui-$EnvironmentAbbreviation'" } `
        -OperationName "Get UI app registration" `
        -MaxAttempts 3 -BaseDelaySeconds 2
    $webApiApp = Invoke-WithRetry `
        -Operation { Get-MgApplication -Filter "displayName eq '$SolutionAbbreviation-webapi-$EnvironmentAbbreviation'" } `
        -OperationName "Get WebAPI app registration" `
        -MaxAttempts 3 -BaseDelaySeconds 2
    $graphApp = Invoke-WithRetry `
        -Operation { Get-MgApplication -Filter "displayName eq '$SolutionAbbreviation-Graph-$EnvironmentAbbreviation'" } `
        -OperationName "Get Graph app registration" `
        -MaxAttempts 3 -BaseDelaySeconds 2
    $teamsChannelApp = Invoke-WithRetry `
        -Operation { Get-MgApplication -Filter "displayName eq '$SolutionAbbreviation-TeamsChannel-$EnvironmentAbbreviation'" } `
        -OperationName "Get Teams Channel app registration" `
        -MaxAttempts 3 -BaseDelaySeconds 2
    $functionAuthApp = Invoke-WithRetry `
        -Operation { Get-MgApplication -Filter "displayName eq '$SolutionAbbreviation-FunctionAuth-$EnvironmentAbbreviation'" } `
        -OperationName "Get FunctionAuth app registration" `
        -MaxAttempts 3 -BaseDelaySeconds 2

    # Check which apps need admin consent based on their required resource access
    $appsThatNeedAdminConsent = @()

    $apps = @(
        $uiApp,
        $webApiApp,
        $graphApp,
        $teamsChannelApp,
        $functionAuthApp
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
        FunctionAuthAppId = $functionAuthApp.AppId
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
        [Parameter(Mandatory = $false)]
        [AllowNull()]
        [AllowEmptyString()]
        [string]$StaticWebAppName,
        [Parameter(Mandatory = $true)]
        [boolean]$SkipPrivilegedDirectoryActions        
    )

    Write-Host "`n🔧 Configuring Web Apps and App Registrations for CORS..." -ForegroundColor Cyan

    $computeResourceGroup = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation"
    $webApiName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-webapi"
    $uiWebAppName = if ([string]::IsNullOrWhiteSpace($StaticWebAppName)) { "$SolutionAbbreviation-ui" } else { $StaticWebAppName }

    # Set CORS for web apps
    $allowedOrigins = @()

    try {
        $customDomain = Invoke-WithRetry `
            -Operation { Get-AzStaticWebAppCustomDomain -Name $uiWebAppName -ResourceGroupName $computeResourceGroup } `
            -OperationName "Get static web app custom domain" `
            -MaxAttempts 3 -BaseDelaySeconds 2
        if (-not [string]::IsNullOrEmpty($customDomain)) {
            $allowedOrigins += "https://$($customDomain.DomainName)"
        }
    }
    catch {
        Write-Output "No custom domain associated with this web app."
    }

    $staticWebApp = Invoke-WithRetry `
        -Operation { Get-AzStaticWebApp -Name $uiWebAppName -ResourceGroupName $computeResourceGroup } `
        -OperationName "Get static web app" `
        -MaxAttempts 3 -BaseDelaySeconds 2
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

    $webApi = Invoke-WithRetry `
        -Operation { Get-AzWebApp -ResourceGroupName $computeResourceGroup -Name $webApiName } `
        -OperationName "Get WebAPI for CORS" `
        -MaxAttempts 3 -BaseDelaySeconds 2
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

        $webApiResource = Invoke-WithRetry `
            -Operation { Get-AzResource @apiResourceParams } `
            -OperationName "Get WebAPI resource for CORS" `
            -MaxAttempts 3 -BaseDelaySeconds 2
        $webApiResource.Properties.siteConfig.cors = @{
            allowedOrigins = $newCORs
        }

        Invoke-WithRetry `
            -Operation { $webApiResource | Set-AzResource -Force } `
            -OperationName "Update WebAPI CORS settings" `
            -MaxAttempts 3 -BaseDelaySeconds 2

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
        [string]$SubscriptionId,
        [Parameter(Mandatory = $false)]
        [AllowNull()]
        [AllowEmptyString()]
        [string]$StaticWebAppName
    )

    $resolvedStaticWebAppName = if ([string]::IsNullOrWhiteSpace($StaticWebAppName)) { "$SolutionAbbreviation-ui" } else { $StaticWebAppName }

    Write-Host "Publishing UI code to Azure Static Web App '$resolvedStaticWebAppName'..." -ForegroundColor Yellow

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
    
    $appInsights = Invoke-WithRetry `
        -Operation { Get-AzApplicationInsights -ResourceGroupName $dataResourceGroup  -Name "$SolutionAbbreviation-data-$EnvironmentAbbreviation" } `
        -OperationName "Get Application Insights" `
        -MaxAttempts 3 -BaseDelaySeconds 2
    $appInsightsConnectionString = $appInsights.ConnectionString

    $appVersionFilePath = "$WebAppDirectory/appVersion.txt"
    $appVersion = ''
    if (Test-Path -Path $appVersionFilePath) {
        $appVersion = Get-Content -Path $appVersionFilePath
        Write-Host "App version (from appVersion.txt): $appVersion"
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
    $envContent += "REACT_APP_VERSION_NUMBER=$appVersion`n"
    $envContent += "DISABLE_ESLINT_PLUGIN=true`n"

    Set-Content -Path "$WebAppDirectory/.env" -Value $envContent -Force
    $currentLocation = Get-Location

    Set-Location -Path $WebAppDirectory

    try {
        # Get the web app deployment token
        $webAppName = $resolvedStaticWebAppName
        $webAppSecrets = Invoke-WithRetry `
            -Operation { (Get-AzStaticWebAppSecret -name $webAppName -ResourceGroupName $computeResourceGroup).Property | ConvertFrom-Json } `
            -OperationName "Get static web app secrets" `
            -MaxAttempts 3 -BaseDelaySeconds 2
        $webAppDeploymentToken = $webAppSecrets.apiKey

        # Build UI if not already built (source-based deploys)
        if (-not (Test-Path "build")) {
            Write-Host "Build directory not found. Restoring dependencies and building UI from source..." -ForegroundColor Yellow
            pnpm install --frozen-lockfile
            pnpm build
        }

        swa deploy "build" --env "Production" -n $webAppName -R $computeResourceGroup --deployment-token $webAppDeploymentToken

        # Verify deployment actually landed
        Write-Host "🔍 Verifying UI deployment..." -ForegroundColor Yellow
        $buildAssetsDir = "$WebAppDirectory/build/assets"
        $expectedJsFile = if (Test-Path $buildAssetsDir) {
            Get-ChildItem -Path $buildAssetsDir -Filter "index-*.js" | Select-Object -First 1
        }

        if (-not $expectedJsFile) {
            Write-Host "⚠ Could not find index-*.js in build/assets — skipping deployment verification." -ForegroundColor Yellow
        } else {
            Write-Host "   Expected asset: $($expectedJsFile.Name)" -ForegroundColor Yellow

            $staticWebApp = Invoke-WithRetry `
                -Operation { Get-AzStaticWebApp -Name $webAppName -ResourceGroupName $computeResourceGroup } `
                -OperationName "Get static web app for verification" `
                -MaxAttempts 3 -BaseDelaySeconds 2
            $swaUrl = "https://$($staticWebApp.DefaultHostname)"

            # Retry verification to allow CDN propagation time
            $maxVerifyAttempts = 5
            $verifyDelaySeconds = 15
            $verified = $false

            for ($attempt = 1; $attempt -le $maxVerifyAttempts; $attempt++) {
                if ($attempt -gt 1) {
                    Write-Host "   Waiting ${verifyDelaySeconds}s for CDN propagation (attempt $attempt/$maxVerifyAttempts)..." -ForegroundColor Yellow
                    Start-Sleep -Seconds $verifyDelaySeconds
                }

                Write-Host "   Checking $swaUrl ..." -ForegroundColor Yellow

                try {
                    $response = Invoke-WebRequest -Uri $swaUrl -UseBasicParsing -TimeoutSec 30 -MaximumRedirection 5 -SkipHttpErrorCheck -Headers @{ "Cache-Control" = "no-cache" }
                    if ($response.StatusCode -ne 200) {
                        Write-Host "⚠ Site returned HTTP $($response.StatusCode) — cannot verify deployment." -ForegroundColor Yellow
                        $verified = $true
                        break
                    } elseif ($response.Content -match [regex]::Escape($expectedJsFile.Name)) {
                        Write-Host "✅ Deployed site references $($expectedJsFile.Name) — deployment verified!" -ForegroundColor Green
                        $verified = $true
                        break
                    } else {
                        Write-Host "⚠ Deployed site does not yet reference $($expectedJsFile.Name) (attempt $attempt/$maxVerifyAttempts)" -ForegroundColor Yellow
                    }
                } catch {
                    Write-Host "⚠ Could not reach $swaUrl to verify deployment (attempt $attempt/$maxVerifyAttempts): $_" -ForegroundColor Yellow
                }
            }

            if (-not $verified) {
                Write-Host "❌ Deployed site does NOT reference $($expectedJsFile.Name) after $maxVerifyAttempts attempts!" -ForegroundColor Red
                Write-Host "   The SWA CLI reported success but the upload may not have landed." -ForegroundColor Red
                throw "Deployment verification FAILED — asset hash mismatch. The UI was NOT deployed successfully."
            }
        }
    } finally {
        Set-Location -Path $currentLocation
    }

    Write-Host "✅ UI code published successfully!" -ForegroundColor Green
}

function Test-ScriptDependencies {
    $dependenciesPresent = $true
    $scriptsDirectory = Split-Path $PSScriptRoot -Parent

    Write-Host "🔍 Checking required dependencies..."

    # PowerShell Core
    if ($PSVersionTable.PSEdition -ne "Core") {
        throw "This script requires PowerShell Core (pwsh). Current edition: $($PSVersionTable.PSEdition)"
    } else {
        Write-Host "✅ Running on PowerShell Core version $($PSVersionTable.PSVersion)" -ForegroundColor Green
    }

    # 64-bit
    if (-not [Environment]::Is64BitProcess) {
        throw "This script must be run in a 64-bit PowerShell session."
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
            throw "pnpm installation failed."
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
        throw "Failed to install swa version $desiredVersion."
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
        throw "One or more dependencies are missing. Please resolve them before continuing."
    }

    Write-Host "🎉 All dependencies verified successfully!" -ForegroundColor Green
}

function Install-RequiredModules {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$ScriptsDirectory
    )

    Write-Host "`n" -NoNewline
    Write-Host ("=" * 60) -ForegroundColor Cyan
    Write-Host "  Installing Required PowerShell Modules" -ForegroundColor Cyan
    Write-Host ("=" * 60) -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  ⏳ This process may take up to 10 minutes depending on" -ForegroundColor Yellow
    Write-Host "     your network speed and whether modules are cached." -ForegroundColor Yellow

    $totalSteps = 3
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

    # Step 1: Install Az modules
    Write-Host "`n  [1/$totalSteps] " -ForegroundColor Magenta -NoNewline
    Write-Host "Az Modules" -ForegroundColor White
    Write-Host "          Installing/Importing..." -ForegroundColor Gray
    . ($ScriptsDirectory + '/Install-AzModuleIfNeeded.ps1')
    Install-AzModuleIfNeeded | Out-Null
    Write-Host "          ✓ Az modules ready" -ForegroundColor Green

    # Step 2: Install Microsoft Graph modules
    Write-Host "`n  [2/$totalSteps] " -ForegroundColor Magenta -NoNewline
    Write-Host "Microsoft Graph Modules" -ForegroundColor White
    . ($ScriptsDirectory + '/Install-ModuleIfNeeded.ps1')

    $requiredGraphModules = @(
        "Microsoft.Graph.Authentication",
        "Microsoft.Graph.Applications",
        "Microsoft.Graph.Identity.DirectoryManagement",
        "Microsoft.Graph.Users"
    )

    $moduleIndex = 0
    $totalModules = $requiredGraphModules.Count

    foreach ($module in $requiredGraphModules) {
        $moduleIndex++
        $shortName = $module -replace '^Microsoft\.Graph\.', ''
        Write-Host "          [$moduleIndex/$totalModules] Installing " -ForegroundColor Gray -NoNewline
        Write-Host "$shortName" -ForegroundColor White -NoNewline
        Write-Host " (v2.17.0)..." -ForegroundColor Gray
        Install-ModuleIfNeeded -Name $module -Version "2.17.0" | Out-Null
        Write-Host "                 ✓ $shortName ready" -ForegroundColor Green
    }

    # Step 3: Install MSIdentityTools
    Write-Host "`n  [3/$totalSteps] " -ForegroundColor Magenta -NoNewline
    Write-Host "MSIdentityTools" -ForegroundColor White
    Write-Host "          Installing " -ForegroundColor Gray -NoNewline
    Write-Host "MSIdentityTools" -ForegroundColor White -NoNewline
    Write-Host " (v2.0.52)..." -ForegroundColor Gray
    Install-ModuleIfNeeded -Name MSIdentityTools -Version "2.0.52" | Out-Null
    Write-Host "          ✓ MSIdentityTools ready" -ForegroundColor Green

    $stopwatch.Stop()
    $elapsed = $stopwatch.Elapsed.ToString("mm\:ss")

    Write-Host "`n" -NoNewline
    Write-Host ("=" * 60) -ForegroundColor Green
    Write-Host "  ✓ All required modules installed ($elapsed)" -ForegroundColor Green
    Write-Host ("=" * 60) -ForegroundColor Green
    Write-Host ""
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
        [bool]$AssertUserPermissions = $true,
        [Parameter(Mandatory = $false)]
        [bool]$SkipAuthentication = $false
    )

    Test-ScriptDependencies

    if ($SkipModuleInstallation -eq $true) {
        Write-Host "Skipping module installation as per configuration [SkipModuleInstallation = $($SkipModuleInstallation)]." -ForegroundColor Yellow
    } else {
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

    if ($SkipAuthentication -eq $true) {
        Write-Host "Skipped authentication as per configuration [SkipAuthentication = $($SkipAuthentication)]." -ForegroundColor Yellow
    }
    else {
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

    # --- Transcript logging ---
    $transcriptStarted = $false
    try {
        $logsDir = Join-Path $PSScriptRoot 'logs'
        if (-not (Test-Path $logsDir)) {
            New-Item -ItemType Directory -Path $logsDir -Force | Out-Null
        }
        $logTimestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
        $logPath = Join-Path $logsDir "deploy-$logTimestamp.log"
        Start-Transcript -Path $logPath -NoClobber
        $transcriptStarted = $true
    } catch {
        Write-Warning "Could not start transcript logging: $_"
    }

    try {

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
    $skipNetworkingDeployment                       = Get-Default -Value $ParameterHashtable['skipNetworkingDeployment'].value -Default $true
    $skipAuthentication                             = Get-Default -Value $ParameterHashtable['skipAuthentication'].value -Default $false

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
        -AssertUserPermissions $assertUserPermissions `
        -SkipAuthentication $skipAuthentication

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

        . "$scriptsDirectory/GMM-WebAPI-Operations.ps1"

        Write-Host "`nStopping GMM via WebApi Stop endpoint before deployment..." -ForegroundColor Yellow
        if($resetGMMType -eq "Credentials" -or $resetGMMType -eq "ServicePrincipal") {
            Invoke-GMMOperation -OperationName "Stop" -AuthMethod $resetGMMType `
                -SolutionAbbreviation $solutionAbbreviation `
                -EnvironmentAbbreviation $environmentAbbreviation
        } else {
            Write-Host "Skipping pre-deployment stop — resetGMMType is '$resetGMMType'." -ForegroundColor Yellow
        }

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
        $isUserAssignedManagedIdentityAuth = if ($parameterHashtable.authenticationType.value -eq "UserAssignedManagedIdentity") { $true } else { $false }
        $bastionVnetAddressPrefix = Get-Default -Value $parameterHashtable['bastionVnetAddressPrefix'].value -Default '10.0.0.0/24'
        Set-RBACPermissions `
        -SolutionAbbreviation $solutionAbbreviation `
        -EnvironmentAbbreviation $environmentAbbreviation `
        -TenantId $parameterHashtable.tenantId.value `
        -ScriptsDirectory "$scriptsDirectory/PostDeploymentRoleAssignments" `
        -SetUserAssignedManagedIdentityPermissions $isUserAssignedManagedIdentityAuth `
        -SkipPrivilegedDirectoryActions $skipPrivilegedDirectoryActions `
        -SkipNetworkingDeployment $skipNetworkingDeployment `
        -BastionVnetAddressPrefix $bastionVnetAddressPrefix
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

    if (!$isInitialDeployment -and $resetGMMType -ne "Skip") {
        Write-Host "`nCalling Reschedule endpoint via WebApi..." -ForegroundColor Yellow
        if ($resetGMMType -eq "Credentials" -or $resetGMMType -eq "ServicePrincipal") {
            Invoke-GMMOperation -OperationName "Reschedule" -AuthMethod $resetGMMType `
                -SolutionAbbreviation $solutionAbbreviation `
                -EnvironmentAbbreviation $environmentAbbreviation
        }
        Write-Host "`nStarting all remaining function apps..." -ForegroundColor Yellow
        Start-FunctionApps -ResourceGroupName $computeResourceGroup
    } elseif ($startFunctions) {
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


    $staticWebApp = Invoke-WithRetry `
        -Operation { Get-AzStaticWebApp -Name "$SolutionAbbreviation-ui" -ResourceGroupName $computeResourceGroup } `
        -OperationName "Get static web app URL" `
        -MaxAttempts 3 -BaseDelaySeconds 2
    if ($null -ne $staticWebApp) {
        Write-Host "`nhttps://$($staticWebApp.DefaultHostname)`n" -ForegroundColor Cyan
    }

    } finally {
        if ($transcriptStarted) {
            Stop-Transcript
        }
    }
}
