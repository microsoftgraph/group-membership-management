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
. (Join-Path $sharedScriptsDirectory 'ReusableModules/DeploymentLogging.ps1')
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

    Write-DeployPhase -Name 'Running Post-Deployment Migrations' -Event Begin
    . ($ScriptsDirectory + '/PostDeploymentMigrations/Set-PostDeploymentMigrations.ps1')
    $currentContext = Get-AzContext
    Set-PostDeploymentMigrations `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -SolutionAbbreviation $SolutionAbbreviation `
        -SubscriptionName $currentContext.Subscription.Name `
        -ConnectionString $ConnectionString
    Write-DeployPhase -Name 'Running Post-Deployment Migrations' -Event End
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

    Write-DeployPhase -Name 'Running Pre-Deployment Migrations' -Event Begin
    . ($ScriptsDirectory + '/PreDeploymentMigrations/Set-PreDeploymentMigrations.ps1')

    Set-PreDeploymentMigrations `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -SyncJobsDBConnectionString $SyncJobsDBConnectionString `
        -ADFDBConnectionString $ADFDBConnectionString `
        -SetRBACPermissions $SetRBACPermissions
    Write-DeployPhase -Name 'Running Pre-Deployment Migrations' -Event End
}

function Set-Subscription {
    param (
        [Parameter(Mandatory = $false)]
        [string]$SubscriptionId,
        [Parameter(Mandatory = $true)]
        [string]$ScriptsDirectory
    )

    if (-not $SubscriptionId) {
        Write-DeployLog -Level Info -Message "Current subscription:`n"
        $currentSubscription = (Get-AzContext).Subscription
        Write-DeployLog -Level Info -Message "$($currentSubscription.Name) -  $($currentSubscription.Id)"
        $SubscriptionId = Read-Host -Prompt "If you would like to use other subscription than '$($currentSubscription.Name)' `nprovide the subscription id, otherwise press enter to continue."
    }

    if ($SubscriptionId) {
        try {
            Set-AzContext -SubscriptionId $SubscriptionId -ErrorAction Stop
            $currentSubscription = (Get-AzContext).Subscription
            Write-DeployLog -Level Info -Message "Selected subscription: $($currentSubscription.Name) - $($currentSubscription.Id)"
        }
        catch {
            Write-DeployLog -Level Error -Message "Failed to set subscription context."
            Write-DeployLog -Level Info -Message "SubscriptionId: $SubscriptionId"
            Write-DeployLog -Level Info -Message "TenantId:       $((Get-AzContext).Tenant.Id)"
            Write-DeployLog -Level Info -Message "Account:        $((Get-AzContext).Account)"
            Write-DeployLog -Level Info -Message "Error:          $($_.Exception.Message)"

            If ($_.Exception.Message -match "Please provide a valid tenant or a valid subscription.") {
                Write-DeployLog -Level Info -Message "This issue is sometimes caused by the user account not having any RBAC permissions on the subscription.`n"
            }

            throw
        }
    }

    return $SubscriptionId;
}

function Set-ResourceProviders {
    foreach ($namespace in @("Microsoft.ServiceBus", "Microsoft.Insights", "Microsoft.OperationalInsights", "Microsoft.AlertsManagement", "Microsoft.Storage", "Microsoft.AppConfiguration", "Microsoft.Sql", "Microsoft.Web", "Microsoft.DataFactory", "Microsoft.SignalRService", "Microsoft.DevTestLab")) {
        Write-DeployLog -Level Info -Message "Checking if the resource provider $namespace is registered..."
        $provider = Invoke-WithRetry `
            -Operation { Get-AzResourceProvider -ProviderNamespace $namespace } `
            -OperationName "Get resource provider $namespace" `
            -MaxAttempts 3 -BaseDelaySeconds 2

        if ($provider.Where({ $_.RegistrationState -ne "Registered" }).Count -gt 0) {
            Write-DeployLog -Level Info -Message "$namespace is not registered. Registering..."
            Invoke-WithRetry `
                -Operation { Register-AzResourceProvider -ProviderNamespace $namespace } `
                -OperationName "Register resource provider $namespace" `
                -MaxAttempts 3 -BaseDelaySeconds 2
        }

        Write-DeployLog -Level Info -Message "$namespace is registered."
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
            Write-DeployLog -Level Info -Message "Added role $RoleDefinitionName to $ObjectId on the $KeyVaultName keyvault."
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
        Write-DeployLog -Level Info -Message "Starting REST deployment to resource group: $ResourceGroupName"
    }
    else {
        Write-DeployLog -Level Info -Message "Starting REST deployment of resource groups."
    }
    
    Write-DeployLog -Level Info -Message "Using template file: $TemplateFilePath"

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
        Write-DeployLog -Level Info -Message "Invoking deployment via REST API..."
        $initialResponse = Invoke-RestMethod -Uri $uri -Method Put -Headers $headers -Body $body

        $maxAttempts = 100
        $delaySeconds = 15
        $attempt = 0
        $provisioningState = $initialResponse.properties.provisioningState

        while ($provisioningState -in @("Accepted", "Running", "InProgress")) {
            Start-Sleep -Seconds $delaySeconds
            $attempt++

            Write-DeployLog -Level Info -Message "Polling deployment status (Attempt $attempt/$maxAttempts)..."
            $statusResponse = Invoke-RestMethod -Uri $uri -Method Get -Headers $headers
            $provisioningState = $statusResponse.properties.provisioningState
            Write-DeployLog -Level Info -Message "Current state: $provisioningState"

            if ($attempt -ge $maxAttempts) {
                throw "Deployment status check timed out after $maxAttempts attempts."
            }
        }

        if ($provisioningState -ne "Succeeded") {
            $opsUri = "$baseUri/operations?api-version=2025-03-01"
            $failedOps = @()
            try {
                $failedOps = @((Invoke-RestMethod -Uri $opsUri -Method Get -Headers $headers).value |
                    Where-Object { $_.properties.provisioningState -eq 'Failed' })
            }
            catch {
                Write-DeployLog -Level Warn -Message "Could not list deployment operations; continuing with deployment-level error handling: $($_.Exception.Message)"
            }

            if ($failedOps.Count -gt 0) {
                foreach ($op in $failedOps) {
                    $resource = $op.properties.targetResource.resourceName
                    $type     = $op.properties.targetResource.resourceType
                    $cause    = $op.properties.statusMessage.error.message

                    $detail = $op.properties.statusMessage.error.details |
                        Where-Object { $_.message -and $_.message -ne $cause } | Select-Object -First 1
                    if ($detail) {
                        $code = if ($detail.code) { "$($detail.code): " } else { "" }
                        $cause = "$cause ($code$($detail.message))"
                    }

                    Write-DeployError -Category 'ARM Operation' -Message "$resource [$type]: $cause"
                }
            }
            elseif ($statusResponse.properties.error) {
                Write-DeployError -Category 'ARM Deployment' -Message "$($statusResponse.properties.error.code): $($statusResponse.properties.error.message)"
            }
            else {
                Write-DeployError -Category 'ARM Deployment' -Message "Deployment reached terminal state '$provisioningState' with no error detail."
            }

            # Deep-link to this deployment's details blade; $baseUri is the deployment's
            # ARM resource id under the management endpoint.
            $deploymentResourceId = $baseUri -replace '^https://management\.azure\.com', ''
            $portalDeploymentUrl = "https://portal.azure.com/#blade/HubsExtension/DeploymentDetailsBlade/id/$([uri]::EscapeDataString($deploymentResourceId))"
            Write-DeployLog -Level Info -Message "Investigate in the Azure Portal: $portalDeploymentUrl"

            throw "Deployment failed. See logs above."
        }

        Write-DeployLog -Level Info -Message "Deployment succeeded."
        return $statusResponse
    }
    catch {
        # The failure branch above already logged structured errors before throwing
        # this sentinel; only log genuinely unexpected exceptions here.
        if ($_.Exception.Message -ne 'Deployment failed. See logs above.') {
            Write-DeployError -Category 'Deployment' -Message "Deployment failed unexpectedly: $_"
        }
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
    
    Write-DeployLog -Level Info -Message "Creating resource groups:"
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

    Write-DeployPhase -Name 'Creating Prereq Resources' -Event Begin
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

    Write-DeployPhase -Name 'Creating Prereq Resources' -Event End
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

    Write-DeployLog -Level Info -Message "Checking VM admin secret presence in Key Vault '$dataResourceGroup'..."
    $usernameExists = Check-IfKeyVaultSecretExists -VaultName $dataResourceGroup -SecretName $vmAdminUsernameSecretName
    $passwordExists = Check-IfKeyVaultSecretExists -VaultName $dataResourceGroup -SecretName $vmAdminPasswordSecretName
    Write-DeployLog -Level Info -Message ("VM admin secret status: vmAdminUsername={0}, vmAdminPassword={1}" -f $(if ($usernameExists) { 'found' } else { 'missing' }), $(if ($passwordExists) { 'found' } else { 'missing' }))
    $setVmAdminSecrets = (-not $usernameExists) -or (-not $passwordExists)

    if ($setVmAdminSecrets) {
        Write-DeployLog -Level Info -Message "One or more VM admin secrets are missing in '$dataResourceGroup'. Generating values for deployment..."

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
        Write-DeployLog -Level Info -Message "VM admin secrets already exist in '$dataResourceGroup'. Reusing existing values."
    }

    # Start the jumpbox VM if it exists and is stopped/deallocated.
    # Auto-shutdown may have turned it off; extensions cannot deploy to a non-running VM.
    $vmName = "$SolutionAbbreviation-networking-$EnvironmentAbbreviation-management-vm"
    $vm = Get-AzVM -ResourceGroupName $networkingResourceGroup -Name $vmName -Status -ErrorAction SilentlyContinue
    if ($null -ne $vm) {
        $powerState = ($vm.Statuses | Where-Object { $_.Code -like 'PowerState/*' }).Code
        if ($powerState -in @('PowerState/deallocated', 'PowerState/stopped')) {
            Write-DeployLog -Level Info -Message "Jumpbox VM '$vmName' is $($powerState -replace 'PowerState/'). Starting it before networking deployment..."
            Start-AzVM -ResourceGroupName $networkingResourceGroup -Name $vmName
            Write-DeployLog -Level Info -Message "Jumpbox VM '$vmName' started successfully."
        }
        else {
            Write-DeployLog -Level Info -Message "Jumpbox VM '$vmName' is in state '$($powerState -replace 'PowerState/')'. No action needed."
        }
    }
    else {
        Write-DeployLog -Level Info -Message "Jumpbox VM '$vmName' not found (first deployment). Skipping VM start."
    }

    # Start the jumpbox VM if it exists and is stopped/deallocated.
    # Auto-shutdown may have turned it off; extensions cannot deploy to a non-running VM.
    $vmName = "$SolutionAbbreviation-networking-$EnvironmentAbbreviation-management-vm"
    $vm = Get-AzVM -ResourceGroupName $networkingResourceGroup -Name $vmName -Status -ErrorAction SilentlyContinue
    if ($null -ne $vm) {
        $powerState = ($vm.Statuses | Where-Object { $_.Code -like 'PowerState/*' }).Code
        if ($powerState -in @('PowerState/deallocated', 'PowerState/stopped')) {
            Write-DeployLog -Level Info -Message "Jumpbox VM '$vmName' is $($powerState -replace 'PowerState/'). Starting it before networking deployment..."
            Start-AzVM -ResourceGroupName $networkingResourceGroup -Name $vmName
            Write-DeployLog -Level Info -Message "Jumpbox VM '$vmName' started successfully."
        }
        else {
            Write-DeployLog -Level Info -Message "Jumpbox VM '$vmName' is in state '$($powerState -replace 'PowerState/')'. No action needed."
        }
    }
    else {
        Write-DeployLog -Level Info -Message "Jumpbox VM '$vmName' not found (first deployment). Skipping VM start."
    }

    Write-DeployPhase -Name 'Creating Networking Resources' -Event Begin
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

    Write-DeployPhase -Name 'Creating Networking Resources' -Event End
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
    
    Write-DeployPhase -Name 'Creating Data Resources' -Event Begin
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

    Write-DeployPhase -Name 'Creating Data Resources' -Event End
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

    Write-DeployPhase -Name 'Setting Key Vault Secrets' -Event Begin
    
    $storageAccountSecretName  = Get-DefaultString -Value $ParameterHashtable.storageAccountSecretName.value -Default "adfStorageAccountName"
    $dataResourceGroup = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $secrets = @("sqlServerMSIConnectionString", $storageAccountSecretName, "sqlServerBasicConnectionString")
    Set-DefaultSecretsIfMissing `
        -KeyVaultName $dataResourceGroup `
        -SecretNames $secrets

    $enableFunctionAuthentication = Get-Default -Value $ParameterHashtable['enableFunctionAuthentication'].value -Default $false

    if ($enableFunctionAuthentication -eq $true) {
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
    }
    else {
        Write-DeployLog -Level Warn -Message "Skipping function authentication setup (enableFunctionAuthentication = false)"
        $ParameterHashtable["functionAuthAppClientId"] = @{ value = '' }
    }

    $ParameterHashtable["enableFunctionAuthentication"] = @{ value = $enableFunctionAuthentication }

    Write-DeployPhase -Name 'Setting Key Vault Secrets' -Event End

    Write-DeployPhase -Name 'Creating Compute Resources' -Event Begin
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

    Write-DeployPhase -Name 'Creating Compute Resources' -Event End
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
    Write-DeployPhase -Name 'Setting ADF Key Vault Secrets' -Event Begin
    $adfDataSecrets = @("azureUserReaderUrl", "azureUserReaderKey", "adfStorageAccountName")
    Set-DefaultSecretsIfMissing `
        -KeyVaultName $dataResourceGroup `
        -SecretNames $adfDataSecrets

    $enableFunctionAuthentication = Get-Default -Value $ParameterHashtable['enableFunctionAuthentication'].value -Default $false

    if ($enableFunctionAuthentication -eq $true) {
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
    }
    else {
        Write-DeployLog -Level Warn -Message "Skipping function authentication for ADF (enableFunctionAuthentication = false)"
        $ParameterHashtable["functionAuthAppClientId"] = @{ value = '' }
    }

    $ParameterHashtable["enableFunctionAuthentication"] = @{ value = $enableFunctionAuthentication }
    
    Write-DeployPhase -Name 'Setting ADF Key Vault Secrets' -Event End

    # Deploy ADF resources
    Write-DeployPhase -Name 'Creating ADF Resources' -Event Begin
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

    Write-DeployPhase -Name 'Creating ADF Resources' -Event End
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

    Write-DeployLog -Level Info -Message "Setting Allowed Identities for Function Authentication"

    $dataResourceGroup = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $computeResourceGroup = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation"

    # Get ADF Managed Identity Principal ID (if ADF is deployed)
    $adfMSIPrincipalId = $null
    if ($SkipAzureDataFactoryDeployment -eq $false) {
        Write-DeployLog -Level Info -Message "Retrieving ADF Managed Identity..."
        $adfResourceName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation-adf"
        $adfResource = Invoke-WithRetry `
            -Operation { Get-AzResource -ResourceGroupName $dataResourceGroup -ResourceType "Microsoft.DataFactory/factories" -Name $adfResourceName -ErrorAction SilentlyContinue } `
            -OperationName "Get ADF resource" `
            -MaxAttempts 3 -BaseDelaySeconds 2
        if ($null -ne $adfResource -and $null -ne $adfResource.Identity) {
            $adfMSIPrincipalId = $adfResource.Identity.PrincipalId
            if (-not [string]::IsNullOrWhiteSpace($adfMSIPrincipalId)) {
                Write-DeployLog -Level Success -Message "ADF Managed Identity: $adfMSIPrincipalId"
            }
        }
        else {
            Write-DeployLog -Level Warn -Message "ADF resource not found or has no managed identity"
        }
    }
    else {
        Write-DeployLog -Level Warn -Message "Skipping ADF identity (ADF deployment was skipped)"
    }

    # Get WebAPI Managed Identity Principal ID
    $webApiMSIPrincipalId = $null
    $webApiResourceName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-webapi"
    Write-DeployLog -Level Info -Message "Retrieving WebAPI Managed Identity..."
    $webApiResource = Invoke-WithRetry `
        -Operation { Get-AzWebApp -ResourceGroupName $computeResourceGroup -Name $webApiResourceName -ErrorAction SilentlyContinue } `
        -OperationName "Get WebAPI resource" `
        -MaxAttempts 3 -BaseDelaySeconds 2
    if ($null -ne $webApiResource -and $null -ne $webApiResource.Identity) {
        $webApiMSIPrincipalId = $webApiResource.Identity.PrincipalId
        if (-not [string]::IsNullOrWhiteSpace($webApiMSIPrincipalId)) {
            Write-DeployLog -Level Success -Message "WebAPI Managed Identity: $webApiMSIPrincipalId"
        }
    }
    else {
        Write-DeployLog -Level Warn -Message "WebAPI resource not found or has no managed identity"
    }

    # Define function apps that need ADF access
    $adfFunctionAppNames = @("AzureUserReader", "NonProdService", "SqlDataChecker")
    $adfFunctionAppNames += $AdditionalAdfFunctionAppNames

    # Define function apps that need WebAPI access
    $webApiFunctionAppNames = @("JobScheduler")
    $webApiFunctionAppNames += $AdditionalWebApiFunctionAppNames

    

    # Update function apps that need ADF access
    if ([string]::IsNullOrWhiteSpace($adfMSIPrincipalId) -eq $false) {
        Write-DeployLog -Level Info -Message "Updating function apps for ADF access..."
        Update-FunctionAppAuthSettings -FunctionAppNames $adfFunctionAppNames `
            -AllowedPrincipalIds @($adfMSIPrincipalId) `
            -SolutionAbbreviation $SolutionAbbreviation `
            -EnvironmentAbbreviation $EnvironmentAbbreviation `
            -SubscriptionId $SubscriptionId
    }
    else {
        Write-DeployLog -Level Warn -Message "No ADF identities found. Skipping ADF function app updates."
    }

    # Update function apps that need WebAPI access
    if ([string]::IsNullOrWhiteSpace($webApiMSIPrincipalId) -eq $false) {
        Write-DeployLog -Level Info -Message "Updating function apps for WebAPI access..."
        Update-FunctionAppAuthSettings -FunctionAppNames $webApiFunctionAppNames `
            -AllowedPrincipalIds @($webApiMSIPrincipalId) `
            -SolutionAbbreviation $SolutionAbbreviation `
            -EnvironmentAbbreviation $EnvironmentAbbreviation `
            -SubscriptionId $SubscriptionId
    }
    else {
        Write-DeployLog -Level Warn -Message "No WebAPI identities found. Skipping WebAPI function app updates."
    }

    Write-DeployLog -Level Success -Message "Function Authentication Identities Updated"
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
            Write-DeployLog -Level Success -Message "Found function app: $fullFunctionName"
        }
        else {
            Write-DeployLog -Level Warn -Message "Function app not found: $fullFunctionName"
        }
    }

    if ($null -eq $functionApps -or $functionApps.Count -eq 0) {
        Write-DeployLog -Level Warn -Message "No Function Apps found."
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

        Write-DeployLog -Level Info -Message "[$functionIndex/$totalFunctions] Updating: $functionAppName"

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

            Write-DeployLog -Level Success -Message "Successfully updated $functionAppName"
        }
        catch {
            Write-DeployLog -Level Error -Message "Failed to update $($functionAppName): $($_.Exception.Message)"
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
        Write-DeployPhase -Name 'Creating Resource Groups' -Event Begin
        Set-ResourceGroups `
            -SubscriptionId $SubscriptionId `
            -Location $Location `
            -ResourceGroupTemplateDirectoryPath $TemplateFilesDirectory `
            -ParameterHashtable $ParameterHashtable `
            -AdditionalParameters $commonParametersObject `
            -SetRBACPermissions $setRBACPermissions
        Write-DeployPhase -Name 'Creating Resource Groups' -Event End
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
        $enableFunctionAuthentication = Get-Default -Value $ParameterHashtable['enableFunctionAuthentication'].value -Default $false
        Save-GMMAppRegistrationSecrets `
            -SolutionAbbreviation $SolutionAbbreviation `
            -EnvironmentAbbreviation $EnvironmentAbbreviation `
            -ScriptsDirectory $ScriptsDirectory `
            -AppTenantId $directoryTenantId `
            -GraphAppCertificateName $graphAppCertificateName `
            -TeamsChannelAppCertificateName $teamsChannelAppCertificateName `
            -SkipPrivilegedDirectoryActions $ParameterHashtable.skipPrivilegedDirectoryActions.value `
            -IsClientSecretAuth $isClientSecretAuth `
            -EnableFunctionAuthentication $enableFunctionAuthentication
            
        Start-Sleep -Seconds 10
    }
    else {
        Write-DeployLog -Level Warn -Message "Skipping app registration secret storage as per configuration [skipAppRegistrationSecretStorage = $($ParameterHashtable.skipAppRegistrationSecretStorage.value)]."
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

    if (-not $ParameterHashtable.isInitialDeployment.value) {
        . ($ScriptsDirectory + '/PostDataDeploymentMigrations/Set-PostDataDeploymentMigrations.ps1')
        Set-PostDataDeploymentMigrations `
            -SolutionAbbreviation $SolutionAbbreviation `
            -EnvironmentAbbreviation $EnvironmentAbbreviation
    }

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
        Write-DeployLog -Level Warn -Message "Skipping networking deployment as per configuration [skipNetworkingDeployment = $skipNetworkingDeployment]."
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
        Write-DeployLog -Level Info -Message "Creating Azure Data Factory resources"
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
        Write-DeployLog -Level Info -Message "Skipping Azure Data Factory deployment as per configuration [skipAzureDataFactoryDeployment = $skipAzureDataFactoryDeployment]."
    }

    $enableFunctionAuthentication = Get-Default -Value $ParameterHashtable['enableFunctionAuthentication'].value -Default $false

    if ($enableFunctionAuthentication -eq $true) {
        Set-FunctionAuthenticationAllowedIdentities `
            -SolutionAbbreviation $SolutionAbbreviation `
            -EnvironmentAbbreviation $EnvironmentAbbreviation `
            -SubscriptionId $SubscriptionId `
            -SkipAzureDataFactoryDeployment $skipAzureDataFactoryDeployment
    }
    else {
        Write-DeployLog -Level Warn -Message "Skipping function authentication allowed identities (enableFunctionAuthentication = false)"
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

    Write-DeployPhase -Name 'Configuring SQL Server Firewall' -Event Begin
    Write-DeployLog -Level Info -Message "Setting SQL Server firewall rule"
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
            Write-DeployLog -Level Info -Message "Added firewall rule for SQL Server"
        } `
        -OperationName "Create SQL firewall rule" `
        -MaxAttempts 3 -BaseDelaySeconds 2 `
        -ExistsMessage "SQL firewall rule '$sqlIPRuleName' already exists on '$sqlServerName'. Skipping."
    Write-DeployPhase -Name 'Configuring SQL Server Firewall' -Event End
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
    Write-DeployPhase -Name 'Granting SQL Database Permissions' -Event Begin
    Write-DeployLog -Level Info -Message "Granting permissions to SQL database"

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

    Write-DeployLog -Level Info -Message "Granting permissions to SQL database for $($context.Account.Id)"
    Invoke-SqlOperationWithFirewallRetry `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -SolutionAbbreviation $SolutionAbbreviation `
        -Operation { 
            $connection.Open()
            [void]$roleCommand.ExecuteNonQuery() 
            $connection.Close()
        }

    $roleCommand.Dispose()
    Write-DeployLog -Level Success -Message "Permissions granted to SQL database for $($context.Account.Id)"

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

        Write-DeployLog -Level Info -Message "Granting permissions to SQL database for $($functionApp.Name)"

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

        Write-DeployLog -Level Success -Message "Permissions granted to SQL database for $($functionApp.Name)"
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

        Write-DeployLog -Level Info -Message "Granting permissions to SQL database for $dataFactoryName"

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

        Write-DeployLog -Level Success -Message "Permissions granted to SQL database for $dataFactoryName"

        foreach ($functionApp in $functionAppsADF) {

            $functionSqlScript = "IF NOT EXISTS (SELECT * FROM sys.database_principals WHERE name = N'$($functionApp.Name)')
            BEGIN
                CREATE USER [$($functionApp.Name)] FROM EXTERNAL PROVIDER
                ALTER ROLE db_datareader ADD MEMBER [$($functionApp.Name)]
                ALTER ROLE db_datawriter ADD MEMBER [$($functionApp.Name)]
            END"

            Write-DeployLog -Level Info -Message "Granting permissions to ADF database for $($functionApp.Name)"

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

            Write-DeployLog -Level Success -Message "Permissions granted to ADF database for $($functionApp.Name)"
        }
    }
    Write-DeployPhase -Name 'Granting SQL Database Permissions' -Event End
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
    Write-DeployPhase -Name 'Granting Resource Permissions' -Event Begin
    Write-DeployLog -Level Info -Message "Granting permissions to resources"

    . ($ScriptsDirectory + '/Set-PostDeploymentRoles.ps1')
    Set-PostDeploymentRoles `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -TenantId $TenantId `
        -SetUserAssignedManagedIdentityPermissions $SetUserAssignedManagedIdentityPermissions `
        -SkipPrivilegedDirectoryActions $SkipPrivilegedDirectoryActions `
        -SkipNetworkingDeployment $SkipNetworkingDeployment `
        -BastionVnetAddressPrefix $BastionVnetAddressPrefix
    Write-DeployPhase -Name 'Granting Resource Permissions' -Event End

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
    Write-DeployPhase -Name 'Publishing Function Apps' -Event Begin

    $functionApps = Invoke-WithRetry `
        -Operation { Get-FunctionAppCompat -ResourceGroupName $ComputeResourceGroup } `
        -OperationName "Get function apps for code deploy" `
        -MaxAttempts 3 -BaseDelaySeconds 2
    foreach ($functionApp in $functionApps) {

        Write-DeployLog -Level Info -Message "Publishing code for function app $($functionApp.Name)"

        $functionName = $functionApp.Name.Split("-")[3]
        $packageFile = "$FunctionsPackagesDirectory/$functionName.zip"

        if (-not (Test-Path $packageFile)) {
            Write-DeployLog -Level Info -Message "Package file not found: $packageFile"
            continue
        }

        Invoke-WithRetry `
            -Operation {
                Publish-AzWebApp -ResourceGroupName $ComputeResourceGroup -Name $functionApp.Name -ArchivePath $packageFile -Force
            } `
            -OperationName "Deploying code for $($functionApp.Name)" `
            -MaxAttempts $maxRetriesForDeploymentOperations `
            -BaseDelaySeconds 2

        Write-DeployLog -Level Success -Message "Successfully published code for function app $($functionApp.Name)`n"

        if ($functionApp.Kind -eq "functionapp") {
            Write-DeployLog -Level Info -Message "Function app $($functionApp.Name) is on Comsumption. Setting functionAppScaleLimit = 1..."
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
            Write-DeployLog -Level Success -Message "Successfully set functionAppScaleLimit for $($functionApp.Name)`n"
        }
        
    }

    # publish web api code
    Write-DeployLog -Level Info -Message "Publishing code for webapi app $ComputeResourceGroup-webapi"
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
    
    Write-DeployLog -Level Success -Message "Successfully published code for web api app $($webApi.Name)`n"

    Write-DeployPhase -Name 'Publishing Function Apps' -Event End
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

    Write-DeployPhase -Name 'Configuring Firewall Rules' -Event Begin

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

            Write-DeployLog -Level Info -Message "Fetching existing rules for $keyVaultName"
            $detailedKeyVault = Invoke-WithRetry `
                -Operation { Get-AzKeyVault -Name $keyVaultName -ResourceGroupName $keyVaultResourceGroup } `
                -OperationName "Get key vault details '$keyVaultName'" `
                -MaxAttempts 3 -BaseDelaySeconds 2
            $existingRules = $detailedKeyVault.NetworkAcls.IpAddressRanges

            # Extract current IP rules
            $existingIps = $existingRules.IpRules | ForEach-Object { $_.IpAddress }

            # Combine existing and new, remove duplicates
            $combinedIpRules = ($existingIps + $newIpRules) | Sort-Object -Unique

            Write-DeployLog -Level Info -Message "Applying updated firewall rules to $keyVaultName"

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

    Write-DeployPhase -Name 'Configuring Firewall Rules' -Event End
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
    $functionApps = Invoke-WithRetry `
        -Operation { Get-FunctionAppCompat -ResourceGroupName $ResourceGroupName } `
        -OperationName "Get function apps to stop" `
        -MaxAttempts 3 -BaseDelaySeconds 2
    foreach ($functionApp in $functionApps) {
        Write-DeployLog -Level Info -Message "Stopping function app $($functionApp.Name)"
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

    Write-DeployPhase -Name 'Starting Function Apps' -Event Begin

    $jobTriggerApp = $null

    $functionApps = Invoke-WithRetry `
        -Operation { Get-FunctionAppCompat -ResourceGroupName $ResourceGroupName } `
        -OperationName "Get function apps to start" `
        -MaxAttempts 3 -BaseDelaySeconds 2
        
    foreach ($functionApp in $functionApps) {
        if ($functionApp.Name -match "JobTrigger") {
            $jobTriggerApp = $functionApp
            Write-DeployLog -Level Info -Message "Skipping $($functionApp.Name) (will start last)"
            continue
        }
        Write-DeployLog -Level Info -Message "Starting function app $($functionApp.Name)"
        Invoke-WithRetry `
            -Operation { Start-FunctionAppCompat -ResourceGroupName $ResourceGroupName -Name $functionApp.Name } `
            -OperationName "Start $($functionApp.Name)" `
            -MaxAttempts 3 -BaseDelaySeconds 2
    }

    if ($null -ne $jobTriggerApp -and -not $SkipJobTrigger) {
        Write-DeployLog -Level Info -Message "Waiting $JobTriggerDelaySeconds seconds before starting JobTrigger..."
        Start-Sleep -Seconds $JobTriggerDelaySeconds
        Write-DeployLog -Level Info -Message "Starting $($jobTriggerApp.Name)"
        Start-FunctionAppCompat -ResourceGroupName $ResourceGroupName -Name $jobTriggerApp.Name
    }

    Write-DeployPhase -Name 'Starting Function Apps' -Event End
}

function Update-AppSettingsVersion {
    param (
        [Parameter(Mandatory = $true)]
        [string]$ComputeResourceGroupName
    )

    Write-DeployPhase -Name 'Checking Function App Settings' -Event Begin
    Write-DeployLog -Level Info -Message "Checking function app settings"
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
                Write-DeployLog -Level Info -Message "Updating $($function.Name) -> $($kvReference.SecretName) to $($latestSecretVersion.Version)"
                $updatedVersion = $settings[$key] -replace $kvReference.Version, $latestSecretVersion.Version
                $updatedSettings = Invoke-WithRetry `
                    -Operation { Update-FunctionAppSettingCompat -Name $function.Name -ResourceGroupName $ComputeResourceGroupName -AppSetting @{$key = $updatedVersion } } `
                    -OperationName "Update setting for $($function.Name)" `
                    -MaxAttempts 3 -BaseDelaySeconds 2
            }
        }
    }

    Write-DeployLog -Level Info -Message "Checking web app settings"
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
                Write-DeployLog -Level Info -Message "Updating $($webApp.Name) -> $key to $($latestSecretVersion.Version)"
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

    Write-DeployPhase -Name 'Checking Function App Settings' -Event End
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

    Write-DeployLog -Level Info -Message "Creating app registrations programmatically...`n"

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
        Write-DeployLog -Level Warn -Message "Skipping FunctionAuth app registration as per configuration."
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
        [string] $TeamsChannelAppCertificateName,
        [Parameter(Mandatory = $false)]
        [boolean]$EnableFunctionAuthentication = $false
    )

    Write-DeployPhase -Name 'Saving App Registration Secrets' -Event Begin
    try {
    $applicationSetupScriptsDirectory = Join-Path $ScriptsDirectory "ApplicationSetupScripts"

    # Retrieve Application IDs
    Write-DeployLog -Level Info -Message "Retrieving App Registration IDs..."
    
    $uiAppId = (Invoke-WithRetry `
        -Operation { Get-MgApplication -Filter "displayName eq '$SolutionAbbreviation-ui-$EnvironmentAbbreviation'" } `
        -OperationName "Get UI app registration" `
        -MaxAttempts 3 -BaseDelaySeconds 2).AppId
    if (-not $uiAppId) {
        Write-Error "UI Application '$SolutionAbbreviation-ui-$EnvironmentAbbreviation' not found"
        return
    }
    Write-DeployLog -Level Info -Message "UI App ID: $uiAppId"

    $webApiAppId = (Invoke-WithRetry `
        -Operation { Get-MgApplication -Filter "displayName eq '$SolutionAbbreviation-webapi-$EnvironmentAbbreviation'" } `
        -OperationName "Get WebAPI app registration" `
        -MaxAttempts 3 -BaseDelaySeconds 2).AppId
    if (-not $webApiAppId) {
        Write-Error "WebAPI Application '$SolutionAbbreviation-webapi-$EnvironmentAbbreviation' not found"
        return
    }
    Write-DeployLog -Level Info -Message "WebAPI App ID: $webApiAppId"

    $graphAppId = (Invoke-WithRetry `
        -Operation { Get-MgApplication -Filter "displayName eq '$SolutionAbbreviation-Graph-$EnvironmentAbbreviation'" } `
        -OperationName "Get Graph app registration" `
        -MaxAttempts 3 -BaseDelaySeconds 2).AppId
    if (-not $graphAppId) {
        Write-Error "Graph Application '$SolutionAbbreviation-Graph-$EnvironmentAbbreviation' not found"
        return
    }
    Write-DeployLog -Level Info -Message "Graph App ID: $graphAppId"

    $teamsChannelAppId = (Invoke-WithRetry `
        -Operation { Get-MgApplication -Filter "displayName eq '$SolutionAbbreviation-TeamsChannel-$EnvironmentAbbreviation'" } `
        -OperationName "Get Teams Channel app registration" `
        -MaxAttempts 3 -BaseDelaySeconds 2).AppId
    if (-not $teamsChannelAppId) {
        Write-Error "Teams Channel Application '$SolutionAbbreviation-TeamsChannel-$EnvironmentAbbreviation' not found"
        return
    }
    Write-DeployLog -Level Info -Message "Teams Channel App ID: $teamsChannelAppId"

    $functionAuthAppId = $null
    if ($EnableFunctionAuthentication -eq $true) {
        $functionAuthAppId = (Invoke-WithRetry `
            -Operation { Get-MgApplication -Filter "displayName eq '$SolutionAbbreviation-FunctionAuth-$EnvironmentAbbreviation'" } `
            -OperationName "Get FunctionAuth app registration" `
            -MaxAttempts 3 -BaseDelaySeconds 2).AppId
        if (-not $functionAuthAppId) {
            Write-Error "FunctionAuth Application '$SolutionAbbreviation-FunctionAuth-$EnvironmentAbbreviation' not found"
            return
        }
        Write-DeployLog -Level Info -Message "FunctionAuth App ID: $functionAuthAppId"
    }
    else {
        Write-DeployLog -Level Warn -Message "Skipping FunctionAuth app (enableFunctionAuthentication = false)"
    }

    $createNewSecrets = $false
    $askForSecretInput = $false
    if ($IsClientSecretAuth -eq $true) {
       if ($SkipPrivilegedDirectoryActions -eq $true) {
            # Ask user if they want to input secrets now
            Write-DeployLog -Level Info -Message "Application Secret Configuration"
            Write-DeployLog -Level Info -Message "You are using Client Secret authentication and the 'SkipPrivilegedDirectoryActions' flag is enabled. The deployment script needs the"
            Write-DeployLog -Level Info -Message "client secrets for each application to store them securely in Key Vault."
            Write-DeployLog -Level Info -Message "Would you like to input the application secrets now?"
            Write-DeployLog -Level Info -Message "(Choose 'No' if you have already saved the secrets in a previous deployment)"
            
            $response = Read-Host "Input application secrets now? (Y/N)"
            
            if ($response -notmatch '^[Yy]') {
                Write-DeployLog -Level Warn -Message "Skipping application secret input."
                Write-DeployLog -Level Info -Message "If you need to update secrets later, you can run this deployment again"
                Write-DeployLog -Level Info -Message "or manually update them in the Key Vault.`n"
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
        Write-DeployLog -Level Warn -Message "MANUAL SECRET INPUT REQUIRED"
        Write-DeployLog -Level Info -Message "Please provide the client secrets that you created manually for each app registration.`n"
        
        Write-DeployLog -Level Info -Message "Please enter the client secrets for the following applications:"
        Write-DeployLog -Level Info -Message "(These secrets will be securely stored in Key Vault)`n"
        
        Write-DeployLog -Level Success -Message "1  UI Application ($SolutionAbbreviation-ui-$EnvironmentAbbreviation)"
        do {
            $appSecret = Read-Host "   Enter UI App Client Secret"
             
            if ([string]::IsNullOrWhiteSpace($appSecret)) {
                Write-DeployLog -Level Error -Message "Secret cannot be empty or contain only whitespace. Please try again."
            }
        } while ([string]::IsNullOrWhiteSpace($appSecret))

        $manualSecrets['UISecret'] = $appSecret

        Write-DeployLog -Level Success -Message "2  WebAPI Application ($SolutionAbbreviation-webapi-$EnvironmentAbbreviation)"
        do {
            $appSecret = Read-Host "   Enter WebAPI App Client Secret"
            if ([string]::IsNullOrWhiteSpace($appSecret)) {
                Write-DeployLog -Level Error -Message "Secret cannot be empty or contain only whitespace. Please try again."
            }
        } while ([string]::IsNullOrWhiteSpace($appSecret))

        $manualSecrets['WebApiSecret'] = $appSecret

        Write-DeployLog -Level Success -Message "3  Graph Application ($SolutionAbbreviation-Graph-$EnvironmentAbbreviation)"
        do {
            $appSecret = Read-Host "   Enter Graph App Client Secret"
            if ([string]::IsNullOrWhiteSpace($appSecret)) {
                Write-DeployLog -Level Error -Message "Secret cannot be empty or contain only whitespace. Please try again."
            }
        } while ([string]::IsNullOrWhiteSpace($appSecret))

        $manualSecrets['GraphSecret'] = $appSecret

        Write-DeployLog -Level Success -Message "4  Teams Channel Application ($SolutionAbbreviation-TeamsChannel-$EnvironmentAbbreviation)"
        do {
            $appSecret = Read-Host "   Enter Teams Channel App Client Secret" 

            if ([string]::IsNullOrWhiteSpace($appSecret)) {
                Write-DeployLog -Level Error -Message "Secret cannot be empty or contain only whitespace. Please try again."
            }
        } while ([string]::IsNullOrWhiteSpace($appSecret))

        $manualSecrets['TeamsChannelSecret'] = $appSecret

        Write-DeployLog -Level Success -Message "All secrets collected. Proceeding to save them to Key Vault...`n"
    }

    # UI Application Secrets
    Write-DeployLog -Level Info -Message "Saving UI Application secrets..."
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

    Write-DeployLog -Level Success -Message "UI Application secrets saved"

    # WebAPI Application Secrets
    Write-DeployLog -Level Info -Message "Saving WebAPI Application secrets..."
    $webApiScriptPath = Join-Path $applicationSetupScriptsDirectory "Set-WebApiAzureADApplication.ps1"
    . $webApiScriptPath

    $webApiSecret = if ($askForSecretInput -eq $true) {$manualSecrets['WebApiSecret']} else {"not-set"}
    Set-WebAPIKeyVaultSecrets -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation `
        -AppTenantId $AppTenantId `
        -WebApiApplicationId $webApiAppId `
        -CreateNewSecret $createNewSecrets `
        -AppSecret $webApiSecret

    Write-DeployLog -Level Success -Message "WebAPI Application secrets saved"

    # Graph Application Secrets
    Write-DeployLog -Level Info -Message "Saving Graph Application secrets..."
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

    Write-DeployLog -Level Success -Message "Graph Application secrets saved"

    # Teams Channel Application Secrets
    Write-DeployLog -Level Info -Message "Saving Teams Channel Application secrets..."

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

    Write-DeployLog -Level Success -Message "Teams Channel Application secrets saved"

    # FunctionAuth Application Secrets
    if ($EnableFunctionAuthentication -eq $true -and $null -ne $functionAuthAppId) {
        Write-DeployLog -Level Info -Message "Saving FunctionAuth Application secrets..."

        $functionAuthScriptPath = Join-Path $applicationSetupScriptsDirectory "Set-FunctionAuthApplication.ps1"
        . $functionAuthScriptPath

        Set-FunctionAuthKeyVaultSecrets `
            -SolutionAbbreviation $SolutionAbbreviation `
            -EnvironmentAbbreviation $EnvironmentAbbreviation `
            -AppTenantId $AppTenantId `
            -FunctionAuthAppClientId $functionAuthAppId

        Write-DeployLog -Level Success -Message "FunctionAuth Application secrets saved"
    }
    else {
        Write-DeployLog -Level Warn -Message "Skipping FunctionAuth secrets (enableFunctionAuthentication = false)"
    }

    Write-DeployLog -Level Success -Message "All app registration secrets have been saved to Key Vault"
    }
    finally {
        # Always emit the phase end, even when an early return above (e.g. an app
        # registration not found) exits the function before this point.
        Write-DeployPhase -Name 'Saving App Registration Secrets' -Event End
    }
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
        [string]$DirectoryTenantId,
        [Parameter(Mandatory = $false)]
        [boolean]$EnableFunctionAuthentication = $false
    )

    Write-DeployLog -Level Warn -Message "MANUAL APP REGISTRATION SETUP REQUIRED"
    Write-DeployLog -Level Info -Message "You have chosen to skip privileged directory actions. This means you need to"
    Write-DeployLog -Level Info -Message "manually create the app registrations or run the setup scripts in a separate"
    Write-DeployLog -Level Info -Message "PowerShell session with appropriate permissions.`n"

    Write-DeployLog -Level Info -Message "Required App Registrations:"
    Write-DeployLog -Level Info -Message "1. UI Application          ($SolutionAbbreviation-ui-$EnvironmentAbbreviation)"
    Write-DeployLog -Level Info -Message "2. WebAPI Application      ($SolutionAbbreviation-webapi-$EnvironmentAbbreviation)"
    Write-DeployLog -Level Info -Message "3. Graph Application       ($SolutionAbbreviation-Graph-$EnvironmentAbbreviation)"
    Write-DeployLog -Level Info -Message "4. Teams Channel App       ($SolutionAbbreviation-TeamsChannel-$EnvironmentAbbreviation)"
    if ($EnableFunctionAuthentication) {
        Write-DeployLog -Level Info -Message "5. FunctionAuth App        ($SolutionAbbreviation-FunctionAuth-$EnvironmentAbbreviation)"
    }

    Write-DeployLog -Level Info -Message "Manual Setup Documentation:"
    Write-DeployLog -Level Info -Message "Please refer to the following documentation for manual setup steps:"
    Write-DeployLog -Level Info -Message "- $ScriptsDirectory/ApplicationSetupScripts/Manual Setup Documentation/UI-Application-Creation-Instructions.md"
    Write-DeployLog -Level Info -Message "- $ScriptsDirectory/ApplicationSetupScripts/Manual Setup Documentation/WebAPI-Application-Creation-Instructions.md"
    Write-DeployLog -Level Info -Message "- $ScriptsDirectory/ApplicationSetupScripts/Manual Setup Documentation/GraphCredentials-Application-Creation-Instructions.md"
    Write-DeployLog -Level Info -Message "- $ScriptsDirectory/ApplicationSetupScripts/Manual Setup Documentation/TeamsChannel-Application-Creation-Instructions.md"
    if ($EnableFunctionAuthentication) {
        Write-DeployLog -Level Info -Message "- $ScriptsDirectory/ApplicationSetupScripts/Manual Setup Documentation/FunctionAuth-Application-Creation-Instructions.md"
    }

    Write-DeployLog -Level Info -Message "PowerShell Script Signatures:"
    Write-DeployLog -Level Info -Message "If you prefer to run the setup scripts, use these commands in a separate"
    Write-DeployLog -Level Info -Message "PowerShell session with Global Administrator or Application Administrator permissions:`n"

    Write-DeployLog -Level Warn -Message "IMPORTANT: Install Required Modules First!"
    Write-DeployLog -Level Info -Message "Before running any of the setup scripts below, you must first install the required"
    Write-DeployLog -Level Info -Message "PowerShell modules. Run these commands in your PowerShell session:`n"

    Write-DeployLog -Level Info -Message "# Install Required Modules"
    Write-DeployLog -Level Info -Message ". `"$ScriptsDirectory/Install-AzModuleIfNeeded.ps1`""
    Write-DeployLog -Level Info -Message "Install-AzModuleIfNeeded"
    Write-DeployLog -Level Info -Message ". `"$ScriptsDirectory/Install-ModuleIfNeeded.ps1`""
    Write-DeployLog -Level Info -Message "Install-ModuleIfNeeded -Name `"Microsoft.Graph.Authentication`" -Version `"2.17.0`""
    Write-DeployLog -Level Info -Message "Install-ModuleIfNeeded -Name `"Microsoft.Graph.Applications`" -Version `"2.17.0`""
    Write-DeployLog -Level Info -Message "Install-ModuleIfNeeded -Name `"Microsoft.Graph.Identity.DirectoryManagement`" -Version `"2.17.0`""
    Write-DeployLog -Level Info -Message "Install-ModuleIfNeeded -Name `"Microsoft.Graph.Users`" -Version `"2.17.0`""
    Write-DeployLog -Level Info -Message "`$global:SkipModuleInstall = `$true`n"

    Write-DeployLog -Level Info -Message "Once the modules are installed, proceed with the app registration scripts:`n"

    Write-DeployLog -Level Success -Message "# 1. UI Application"
    Write-DeployLog -Level Info -Message ". `"$ScriptsDirectory/ApplicationSetupScripts/Set-UIAzureADApplication.ps1`""
    Write-DeployLog -Level Info -Message "Set-UIAzureADApplication ``"
    Write-DeployLog -Level Info -Message "-SolutionAbbreviation `"$SolutionAbbreviation`" ``"
    Write-DeployLog -Level Info -Message "-EnvironmentAbbreviation `"$EnvironmentAbbreviation`" ``"
    Write-DeployLog -Level Info -Message "-AppTenantId `"$DirectoryTenantId`" ``"
    Write-DeployLog -Level Info -Message "-SaveToKeyVault `$false ``"
    Write-DeployLog -Level Info -Message "-SkipIfApplicationExists `$false ``"
    Write-DeployLog -Level Info -Message "-Clean `$false`n"

    Write-DeployLog -Level Success -Message "# 2. WebAPI Application"
    Write-DeployLog -Level Info -Message ". `"$ScriptsDirectory/ApplicationSetupScripts/Set-WebApiAzureADApplication.ps1`""
    Write-DeployLog -Level Info -Message "Set-WebApiAzureADApplication ``"
    Write-DeployLog -Level Info -Message "-SolutionAbbreviation `"$SolutionAbbreviation`" ``"
    Write-DeployLog -Level Info -Message "-EnvironmentAbbreviation `"$EnvironmentAbbreviation`" ``"
    Write-DeployLog -Level Info -Message "-AppTenantId `"$DirectoryTenantId`" ``"
    Write-DeployLog -Level Info -Message "-SaveToKeyVault `$false ``"
    Write-DeployLog -Level Info -Message "-SkipIfApplicationExists `$false ``"
    Write-DeployLog -Level Info -Message "-Clean `$false`n"

    Write-DeployLog -Level Success -Message "# 3. Graph Application"
    Write-DeployLog -Level Info -Message ". `"$ScriptsDirectory/ApplicationSetupScripts/Set-GraphCredentialsAzureADApplication.ps1`""
    Write-DeployLog -Level Info -Message "Set-GraphCredentialsAzureADApplication ``"
    Write-DeployLog -Level Info -Message "-SolutionAbbreviation `"$SolutionAbbreviation`" ``"
    Write-DeployLog -Level Info -Message "-EnvironmentAbbreviation `"$EnvironmentAbbreviation`" ``"
    Write-DeployLog -Level Info -Message "-AppTenantId `"$DirectoryTenantId`" ``"
    Write-DeployLog -Level Info -Message "-SaveToKeyVault `$false ``"
    Write-DeployLog -Level Info -Message "-SkipIfApplicationExists `$false ``"
    Write-DeployLog -Level Info -Message "-Clean `$false`n"

    Write-DeployLog -Level Success -Message "# 4. Teams Channel Application"
    Write-DeployLog -Level Info -Message ". `"$ScriptsDirectory/ApplicationSetupScripts/Set-TeamsChannelAzureADApplication.ps1`""
    Write-DeployLog -Level Info -Message "Set-TeamsChannelAzureADApplication ``"
    Write-DeployLog -Level Info -Message "-SolutionAbbreviation `"$SolutionAbbreviation`" ``"
    Write-DeployLog -Level Info -Message "-EnvironmentAbbreviation `"$EnvironmentAbbreviation`" ``"
    Write-DeployLog -Level Info -Message "-AppTenantId `"$DirectoryTenantId`" ``"
    Write-DeployLog -Level Info -Message "-SaveToKeyVault `$false ``"
    Write-DeployLog -Level Info -Message "-SkipIfApplicationExists `$false ``"
    Write-DeployLog -Level Info -Message "-Clean `$false`n"

    if ($EnableFunctionAuthentication) {
        Write-DeployLog -Level Success -Message "# 5. FunctionAuth Application"
        Write-DeployLog -Level Info -Message ". `"$ScriptsDirectory/ApplicationSetupScripts/Set-FunctionAuthApplication.ps1`""
        Write-DeployLog -Level Info -Message "Set-FunctionAuthApplication ``"
        Write-DeployLog -Level Info -Message "-SolutionAbbreviation `"$SolutionAbbreviation`" ``"
        Write-DeployLog -Level Info -Message "-EnvironmentAbbreviation `"$EnvironmentAbbreviation`" ``"
        Write-DeployLog -Level Info -Message "-AppTenantId `"$DirectoryTenantId`" ``"
        Write-DeployLog -Level Info -Message "-SaveToKeyVault `$false ``"
        Write-DeployLog -Level Info -Message "-SkipIfApplicationExists `$false ``"
        Write-DeployLog -Level Info -Message "-Clean `$false`n"
    }

    if ($IsClientSecretAuth -eq $true) {
        Write-DeployLog -Level Info -Message "Creating Application Secrets (Client Secret Authentication)"
        Write-DeployLog -Level Info -Message "You need to manually create a client secret for each application registration."
        Write-DeployLog -Level Warn -Message "IMPORTANT: Save the secret values immediately after creation!"
        Write-DeployLog -Level Warn -Message "You will be prompted to input these secrets later in this deployment."
        Write-DeployLog -Level Warn -Message "Secret values cannot be retrieved after you navigate away from the creation screen."
        Write-DeployLog -Level Info -Message "Documentation Location:"
        Write-DeployLog -Level Info -Message "$ScriptsDirectory/ApplicationSetupScripts/Manual Setup Documentation"
        Write-DeployLog -Level Info -Message "Each application folder contains detailed instructions on creating client secrets."
    }

    Write-DeployLog -Level Info -Message "Once you have completed the app registrations setup, press ENTER to continue..."
    Write-DeployLog -Level Info -Message "(The script will validate all app registrations before proceeding)`n"
    
    $null = Read-Host

    Write-DeployLog -Level Info -Message "Validating App Registrations..."

    # Source the validation scripts
    . ($ScriptsDirectory + '/ApplicationSetupScripts/Set-UIAzureADApplication.ps1')
    . ($ScriptsDirectory + '/ApplicationSetupScripts/Set-WebApiAzureADApplication.ps1')
    . ($ScriptsDirectory + '/ApplicationSetupScripts/Set-GraphCredentialsAzureADApplication.ps1')
    . ($ScriptsDirectory + '/ApplicationSetupScripts/Set-TeamsChannelAzureADApplication.ps1')
    if ($EnableFunctionAuthentication) {
        . ($ScriptsDirectory + '/ApplicationSetupScripts/Set-FunctionAuthApplication.ps1')
    }

    # Validate each application
    $uiValid = Test-UIApplication -SolutionAbbreviation $SolutionAbbreviation -EnvironmentAbbreviation $EnvironmentAbbreviation
    $webApiValid = Test-WebApiApplication -SolutionAbbreviation $SolutionAbbreviation -EnvironmentAbbreviation $EnvironmentAbbreviation
    $graphValid = Test-GraphCredentialsApplication -SolutionAbbreviation $SolutionAbbreviation -EnvironmentAbbreviation $EnvironmentAbbreviation
    $teamsChannelValid = Test-TeamsChannelApplication -SolutionAbbreviation $SolutionAbbreviation -EnvironmentAbbreviation $EnvironmentAbbreviation
    $functionAuthValid = if ($EnableFunctionAuthentication) {
        Test-FunctionAuthApplication -SolutionAbbreviation $SolutionAbbreviation -EnvironmentAbbreviation $EnvironmentAbbreviation
    } else { $true }

    Write-DeployLog -Level Info -Message "Validation Summary:"
    Write-DeployLog -Level Info -Message "   UI Application:           $(if ($uiValid) { 'PASS' } else { 'FAIL' })"
    Write-DeployLog -Level Info -Message "   WebAPI Application:       $(if ($webApiValid) { 'PASS' } else { 'FAIL' })"
    Write-DeployLog -Level Info -Message "   Graph Application:        $(if ($graphValid) { 'PASS' } else { 'FAIL' })"
    Write-DeployLog -Level Info -Message "   Teams Channel Application: $(if ($teamsChannelValid) { 'PASS' } else { 'FAIL' })"
    if ($EnableFunctionAuthentication) {
        Write-DeployLog -Level Info -Message "   FunctionAuth Application: $(if ($functionAuthValid) { 'PASS' } else { 'FAIL' })"
    } else {
        Write-DeployLog -Level Info -Message "FunctionAuth Application:   SKIPPED (enableFunctionAuthentication = false)"
    }

    if (-not ($uiValid -and $webApiValid -and $graphValid -and $teamsChannelValid -and $functionAuthValid)) {
        Write-DeployLog -Level Error -Message "One or more applications failed validation. Please review the errors above and fix the issues."
        Write-DeployLog -Level Info -Message "You can re-run the validation by calling the Test-*Application functions individually.`n"
        throw "App registration validation failed. Please fix the issues and try again."
    }

    Write-DeployLog -Level Success -Message "All app registrations validated successfully!`n"

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
        [boolean] $SkipPrivilegedDirectoryActions,
        [Parameter(Mandatory = $false)]
        [boolean] $EnableFunctionAuthentication = $false
    )

    Write-DeployPhase -Name 'Creating/Updating App Registrations' -Event Begin
    $appCreationResult = $null
    if ($SkipPrivilegedDirectoryActions -eq $true) {
        # Manual flow - prompt user to create app registrations
        $appCreationResult = Set-GMMAppRegistrationsManually `
            -SolutionAbbreviation $SolutionAbbreviation `
            -EnvironmentAbbreviation $EnvironmentAbbreviation `
            -ScriptsDirectory $ScriptsDirectory `
            -IsClientSecretAuth $IsClientSecretAuth `
            -DirectoryTenantId $DirectoryTenantId `
            -EnableFunctionAuthentication $EnableFunctionAuthentication
    }
    else {
        # Normal flow - create app registrations programmatically
        $skipFunctionAuthAppParam = @{}
        if ($EnableFunctionAuthentication -eq $false) {
            $skipFunctionAuthAppParam = @{ SkipFunctionAuthApp = $true }
        }
        $appCreationResult = Set-GMMAppRegistrationsProgrammatically `
            -SolutionAbbreviation $SolutionAbbreviation `
            -EnvironmentAbbreviation $EnvironmentAbbreviation `
            -ScriptsDirectory $ScriptsDirectory `
            -DirectoryTenantId $DirectoryTenantId `
            @skipFunctionAuthAppParam
    }

    Write-DeployLog -Level Success -Message "App registrations created successfully!`n"
    Write-DeployPhase -Name 'Creating/Updating App Registrations' -Event End
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

    Write-DeployLog -Level Warn -Message "MANUAL REDIRECT URI UPDATE REQUIRED"
    Write-DeployLog -Level Info -Message "The following redirect URIs need to be added to the UI application:"
    Write-DeployLog -Level Info -Message "Application: $SolutionAbbreviation-ui-$EnvironmentAbbreviation"
    Write-DeployLog -Level Info -Message "Application (client) ID: $UIAppRegistrationId`n"
    
    Write-DeployLog -Level Info -Message "Direct link to app registration:"
    Write-DeployLog -Level Info -Message "$portalLink`n"
    
    Write-DeployLog -Level Info -Message "Redirect URIs to add:"
    foreach ($uri in $RedirectUris) {
        Write-DeployLog -Level Info -Message "$uri"
    }
    
    Write-DeployLog -Level Info -Message "Manual Steps:"
    Write-DeployLog -Level Info -Message "1. Click the direct link above or go to: https://portal.azure.com"
    Write-DeployLog -Level Info -Message "2. If using the portal link directly:"
    Write-DeployLog -Level Info -Message "- Navigate to Microsoft Entra ID > App registrations"
    Write-DeployLog -Level Info -Message "- Find and select: $SolutionAbbreviation-ui-$EnvironmentAbbreviation"
    Write-DeployLog -Level Info -Message "- Go to 'Authentication' in the left menu"
    Write-DeployLog -Level Info -Message "3. Under 'Single-page application', click 'Add URI'"
    Write-DeployLog -Level Info -Message "4. Add each of the redirect URIs listed above"
    Write-DeployLog -Level Info -Message "5. Click 'Save' at the top of the page`n"
    
    Write-DeployLog -Level Info -Message "Press ENTER once you have added the redirect URIs..."
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

    Write-DeployPhase -Name 'Configuring CORS' -Event Begin
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

        Write-DeployLog -Level Success -Message "SignalR service CORS settings updated successfully"
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

        Write-DeployLog -Level Success -Message "WebAPI CORS settings updated successfully"
    }
    else {
        Write-DeployLog -Level Info -Message "No new CORS origins to add to WebAPI"
    }

    # Retrieve UI App Registration ID from Key Vault if not provided
    if ([string]::IsNullOrWhiteSpace($UIAppRegistrationId)) {
        Write-DeployLog -Level Info -Message "UI App Registration ID not provided. Retrieving from Key Vault..."
        $UIAppRegistrationId = Get-KeyVaultSecretWithFirewallRetry `
            -ResourceGroup "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation" `
            -VaultName "$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation" `
            -SecretName "uiAppId" `
            -AsPlainText
        
        if ([string]::IsNullOrWhiteSpace($UIAppRegistrationId)) {
            Write-Error "Unable to retrieve UI App Registration ID from Key Vault"
            return
        }
        Write-DeployLog -Level Info -Message "Retrieved UI App ID: $UIAppRegistrationId"
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
            Write-DeployLog -Level Info -Message "Updating UI application redirect URIs..."
            Update-MgApplication `
                -ApplicationId $uiApp.Id `
                -Spa @{ RedirectUris = $newRedirectUris }
            Write-DeployLog -Level Success -Message "Redirect URIs updated successfully"
        }
    }
    Write-DeployPhase -Name 'Configuring CORS' -Event End
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

    Write-DeployPhase -Name 'Publishing UI' -Event Begin
    Write-DeployLog -Level Info -Message "Publishing UI code to Azure Static Web App '$resolvedStaticWebAppName'..."

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
        Write-DeployLog -Level Info -Message "App version (from appVersion.txt): $appVersion"
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

        # Build UI if not already built (source-based deploys).
        # Resolve the pinned pnpm version from package.json and invoke it via npx so we
        # bypass pnpm's package-manager self-activation. That self-activation performs an
        # npm registry signature check which fails on agents whose npm mirror does not
        # proxy the signature ("Refusing to run pnpm@x: its npm registry signature could
        # not be verified"), silently producing no build output.
        if (-not (Test-Path "build")) {
            Write-DeployLog -Level Warn -Message "Build directory not found. Restoring dependencies and building UI from source..."

            $packageJson = Get-Content -Path "package.json" -Raw | ConvertFrom-Json
            $packageManager = $packageJson.packageManager
            if ([string]::IsNullOrWhiteSpace($packageManager) -or $packageManager -notlike "pnpm@*") {
                throw "Could not resolve a pinned pnpm version from package.json 'packageManager' field (value: '$packageManager')."
            }
            # Strip any '+sha512...' integrity suffix, leaving e.g. 'pnpm@10.34.4'.
            $pnpmSpec = ($packageManager -split '\+')[0]
            Write-DeployLog -Level Info -Message "Using pinned package manager: $pnpmSpec"

            npx --yes $pnpmSpec install --frozen-lockfile
            if ($LASTEXITCODE -ne 0) {
                throw "UI dependency install failed (npx $pnpmSpec install) with exit code $LASTEXITCODE. The UI was NOT deployed."
            }

            npx --yes $pnpmSpec build
            if ($LASTEXITCODE -ne 0) {
                throw "UI build failed (npx $pnpmSpec build) with exit code $LASTEXITCODE. The UI was NOT deployed."
            }
        }

        if (-not (Test-Path "build")) {
            throw "UI build directory 'build' does not exist after the build step. The UI was NOT deployed."
        }

        swa deploy "build" --env "Production" -n $webAppName -R $computeResourceGroup --deployment-token $webAppDeploymentToken

        # Verify deployment actually landed
        Write-DeployLog -Level Info -Message "Verifying UI deployment..."
        $buildAssetsDir = "$WebAppDirectory/build/assets"
        $expectedJsFile = if (Test-Path $buildAssetsDir) {
            # NOTE: This "index-*.js" pattern assumes Vite/Rollup's current default asset
            # naming (hashed "index-<hash>.js" entry bundle). If the Vite major version or the
            # build output config (e.g. rollupOptions.output entryFileNames/assetFileNames)
            # changes the emitted bundle name/format, this check may need to be updated.
            Get-ChildItem -Path $buildAssetsDir -Filter "index-*.js" | Select-Object -First 1
        }

        if (-not $expectedJsFile) {
            throw "No index-*.js found in build/assets after build — the UI bundle was not produced. The UI was NOT deployed."
        } else {
            Write-DeployLog -Level Info -Message "Expected asset: $($expectedJsFile.Name)"

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
                    Write-DeployLog -Level Info -Message "Waiting ${verifyDelaySeconds}s for CDN propagation (attempt $attempt/$maxVerifyAttempts)..."
                    Start-Sleep -Seconds $verifyDelaySeconds
                }

                Write-DeployLog -Level Info -Message "Checking $swaUrl ..."

                try {
                    $response = Invoke-WebRequest -Uri $swaUrl -UseBasicParsing -TimeoutSec 30 -MaximumRedirection 5 -SkipHttpErrorCheck -Headers @{ "Cache-Control" = "no-cache" }
                    if ($response.StatusCode -ne 200) {
                        Write-DeployLog -Level Warn -Message "Site returned HTTP $($response.StatusCode) - cannot verify deployment."
                        $verified = $true
                        break
                    } elseif ($response.Content -match [regex]::Escape($expectedJsFile.Name)) {
                        Write-DeployLog -Level Success -Message "Deployed site references $($expectedJsFile.Name) - deployment verified!"
                        $verified = $true
                        break
                    } else {
                        Write-DeployLog -Level Warn -Message "Deployed site does not yet reference $($expectedJsFile.Name) (attempt $attempt/$maxVerifyAttempts)"
                    }
                } catch {
                    Write-DeployLog -Level Warn -Message "Could not reach $swaUrl to verify deployment (attempt $attempt/$maxVerifyAttempts): $_"
                }
            }

            if (-not $verified) {
                Write-DeployLog -Level Error -Message "Deployed site does NOT reference $($expectedJsFile.Name) after $maxVerifyAttempts attempts!"
                Write-DeployLog -Level Error -Message "The SWA CLI reported success but the upload may not have landed."
                throw "Deployment verification FAILED - asset hash mismatch. The UI was NOT deployed successfully."
            }
        }
    } finally {
        Set-Location -Path $currentLocation
    }

    Write-DeployLog -Level Success -Message "UI code published successfully!"
    Write-DeployPhase -Name 'Publishing UI' -Event End
}

function Test-ScriptDependencies {
    Write-DeployPhase -Name 'Verifying Dependencies' -Event Begin
    $dependenciesPresent = $true
    $scriptsDirectory = Split-Path $PSScriptRoot -Parent

    Write-DeployLog -Level Info -Message "Checking required dependencies..."

    # PowerShell Core
    if ($PSVersionTable.PSEdition -ne "Core") {
        throw "This script requires PowerShell Core (pwsh). Current edition: $($PSVersionTable.PSEdition)"
    } else {
        Write-DeployLog -Level Success -Message "Running on PowerShell Core version $($PSVersionTable.PSVersion)"
    }

    # 64-bit
    if (-not [Environment]::Is64BitProcess) {
        throw "This script must be run in a 64-bit PowerShell session."
    } else {
        Write-DeployLog -Level Success -Message "Running in a 64-bit PowerShell session."
    }

    # Node.js
    if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
        Write-Error "Node.js is not installed. Download it from https://nodejs.org/."
        $dependenciesPresent = $false
    } else {
        Write-DeployLog -Level Success -Message "Node.js $((node -v).Trim()) is installed."
    }

    # npm
    if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
        Write-Error "npm is not installed. It should be included with Node.js."
        $dependenciesPresent = $false
    } else {
        Write-DeployLog -Level Success -Message "npm $((npm -v).Trim()) is installed."
    }

    # pnpm
    if (-not (Get-Command pnpm -ErrorAction SilentlyContinue)) {
        Write-Warning "pnpm is not installed. Attempting to install..."
        npm install -g pnpm

        if ($LASTEXITCODE -ne 0) {
            throw "pnpm installation failed."
        }

        Write-DeployLog -Level Success -Message "pnpm installed successfully."
    } else {
        Write-DeployLog -Level Success -Message "pnpm is already installed."
    }

    # swa CLI
    $desiredVersion = "2.0.5"
    $swaInstalled = Get-Command swa -ErrorAction SilentlyContinue

    if ($swaInstalled) {
        $installedVersion = (npm list -g @azure/static-web-apps-cli --depth=0 | 
            Select-String -Pattern "@azure/static-web-apps-cli@([\d\.]+)" | 
            ForEach-Object { $_.Matches[0].Groups[1].Value })

        if ($installedVersion -eq $desiredVersion) {
            Write-DeployLog -Level Success -Message "swa version $desiredVersion is already installed."
        } else {
            Write-Warning "swa version is $installedVersion, expected $desiredVersion. Updating..."
            npm install -g @azure/static-web-apps-cli@$desiredVersion
        }
    } else {
        Write-DeployLog -Level Info -Message "Installing swa CLI version $desiredVersion..."
        npm install -g @azure/static-web-apps-cli@$desiredVersion
    }

    # Confirm swa version installed
    $installedVersion = (npm list -g @azure/static-web-apps-cli --depth=0 | 
        Select-String -Pattern "@azure/static-web-apps-cli@([\d\.]+)" | 
        ForEach-Object { $_.Matches[0].Groups[1].Value })

    if ($installedVersion -eq $desiredVersion) {
        Write-DeployLog -Level Success -Message "swa version $desiredVersion installed successfully."
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
            Write-Error "Missing: $($item.Key) at $($item.Value)"
            $dependenciesPresent = $false
        } else {
            Write-DeployLog -Level Success -Message "Found $($item.Key)."
        }
    }

    if (-not $dependenciesPresent) {
        throw "One or more dependencies are missing. Please resolve them before continuing."
    }

    Write-DeployLog -Level Success -Message "All dependencies verified successfully!"
    Write-DeployPhase -Name 'Verifying Dependencies' -Event End
}

function Install-RequiredModules {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$ScriptsDirectory
    )

    Write-DeployPhase -Name 'Installing Required Modules' -Event Begin
    Write-DeployLog -Level Warn -Message "This may take up to 10 minutes depending on network speed and whether modules are cached."

    $totalSteps = 3
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

    # Step 1: Install Az modules
    Write-DeployLog -Level Info -Message "[1/$totalSteps] Az Modules"
    Write-DeployLog -Level Info -Message "Installing/Importing..."
    . ($ScriptsDirectory + '/Install-AzModuleIfNeeded.ps1')
    Install-AzModuleIfNeeded | Out-Null
    Write-DeployLog -Level Success -Message "Az modules ready"

    # Step 2: Install Microsoft Graph modules
    Write-DeployLog -Level Info -Message "[2/$totalSteps] Microsoft Graph Modules"
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
        Write-DeployLog -Level Info -Message "[$moduleIndex/$totalModules] Installing $shortName (v2.17.0)..."
        Install-ModuleIfNeeded -Name $module -Version "2.17.0" | Out-Null
        Write-DeployLog -Level Success -Message "$shortName ready"
    }

    # Step 3: Install MSIdentityTools
    Write-DeployLog -Level Info -Message "[3/$totalSteps] MSIdentityTools"
    Write-DeployLog -Level Info -Message "Installing MSIdentityTools (v2.0.52)..."
    Install-ModuleIfNeeded -Name MSIdentityTools -Version "2.0.52" | Out-Null
    Write-DeployLog -Level Success -Message "MSIdentityTools ready"

    $stopwatch.Stop()
    $elapsed = $stopwatch.Elapsed.ToString("mm\:ss")

    Write-DeployLog -Level Success -Message "All required modules installed ($elapsed)"

    Write-DeployPhase -Name 'Installing Required Modules' -Event End
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
        Write-DeployLog -Level Warn -Message "Skipping module installation as per configuration [SkipModuleInstallation = $($SkipModuleInstallation)]."
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
        Write-DeployLog -Level Warn -Message "Skipped authentication as per configuration [SkipAuthentication = $($SkipAuthentication)]."
    }
    else {
        Write-DeployPhase -Name 'Authenticating' -Event Begin
        Write-DeployLog -Level Info -Message "Disconnecting any existing Microsoft Graph sessions..."
        Disconnect-MgGraph -ErrorAction SilentlyContinue | Out-Null

        # Connect to Microsoft Graph and Azure
        if ($UseDeviceAuthentication -eq $true) {
            Write-DeployLog -Level Info -Message "Connecting to Microsoft Graph using device code authentication..."
            Connect-MgGraph -Scopes $requiredScopes -NoWelcome -UseDeviceCode

            Write-DeployLog -Level Info -Message "Connecting to Azure using device code authentication..."
            Connect-AzAccount -UseDeviceAuthentication
        }
        else {
            Write-DeployLog -Level Info -Message "Connecting to Microsoft Graph using interactive authentication..."
            Connect-MgGraph -Scopes $requiredScopes -NoWelcome
            Write-DeployLog -Level Info -Message "Connecting to Azure using interactive authentication..."
            Connect-AzAccount
        }

        Set-Subscription `
                -ScriptsDirectory $ScriptsDirectory `
                -SubscriptionId $SubscriptionId

        Write-DeployPhase -Name 'Authenticating' -Event End

        if ($AssertUserPermissions -eq $true) {
            Write-DeployPhase -Name 'Verifying Permissions' -Event Begin
            if (-not $SkipPrivilegedDirectoryActions) {
                . ($ScriptsDirectory + '/Assert-MicrosoftGraphPermissions.ps1')
                Assert-MicrosoftGraphPermissions
            }

            . ($ScriptsDirectory + '/Assert-RbacPermissionsForDeployment.ps1')
            Assert-RbacPermissionsForDeployment `
                -SolutionAbbreviation $SolutionAbbreviation `
                -EnvironmentAbbreviation $EnvironmentAbbreviation
            Write-DeployPhase -Name 'Verifying Permissions' -Event End
        }
    }
}

function Assert-RequiredParameters {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [hashtable]$ParameterHashtable
    )

    Write-DeployLog -Level Info -Message "Verifying required parameters are provided..."

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

    Write-DeployLog -Level Success -Message "All required parameters are provided."
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
    Write-DeployPhase -Name 'Running EF Migrations' -Event Begin
    Try{
        Write-DeployLog -Level Info -Message "Invoking WebAPI to perform EF migrations..."
        Invoke-WebRequest -Uri "https://$SolutionAbbreviation-compute-$EnvironmentAbbreviation-webapi.azurewebsites.net/" | Out-Null
    }
    Catch{
        # Ignore the error
    }
    Finally {
       Write-DeployLog -Level Success -Message "WebAPI invocation completed."
    }

    Write-DeployPhase -Name 'Running EF Migrations' -Event End
}


function Deploy-Resources {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $false)]
        [string]$ParameterFileName = "parameters.json"
    )

    # --- Transcript logging ---
    # Skip starting a transcript when a parent wrapper (e.g. the private deployment
    # script) already owns a spanning transcript, signalled via
    # $global:GmmDeployTranscriptActive. In that case the parent captures this
    # output and is responsible for Stop-Transcript, so we leave
    # $transcriptStarted = $false and our finally will not stop the parent's
    # transcript. For standalone public runs the flag is unset/false and this
    # behaves exactly as before (public owns its own transcript).
    $transcriptStarted = $false
    if (-not $global:GmmDeployTranscriptActive) {
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
    }

    try {

    $global:GmmCurrentDeployPhase = $null
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
    $enableFunctionAuthentication                   = Get-Default -Value $ParameterHashtable['enableFunctionAuthentication'].value -Default $false

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
        Write-DeployPhase -Name 'Registering Resource Providers' -Event Begin
        Set-ResourceProviders
        Write-DeployPhase -Name 'Registering Resource Providers' -Event End
    }

    if ($parameterHashtable.skipAppRegistrationSetup.value -ne $true) {
        $isClientSecretAuth = if ($ParameterHashtable.authenticationType.value -eq "ClientSecret") { $true } else { $false }
        $appRegistrationSetupResult = Set-GMMAppRegistrations `
                                        -SolutionAbbreviation $solutionAbbreviation `
                                        -EnvironmentAbbreviation $environmentAbbreviation `
                                        -ScriptsDirectory $scriptsDirectory `
                                        -IsClientSecretAuth $isClientSecretAuth `
                                        -DirectoryTenantId $directoryTenantId `
                                        -SkipPrivilegedDirectoryActions $skipPrivilegedDirectoryActions `
                                        -EnableFunctionAuthentication $enableFunctionAuthentication
    }
    else {
        Write-DeployLog -Level Warn -Message "Skipping App Registration setup as per configuration [skipAppRegistrationSetup = $($parameterHashtable.skipAppRegistrationSetup.value)]."
    }

    if(!$isInitialDeployment) {

        . "$scriptsDirectory/GMM-WebAPI-Operations.ps1"

        Write-DeployLog -Level Info -Message "Stopping GMM via WebApi Stop endpoint before deployment..."
        if($resetGMMType -eq "Credentials" -or $resetGMMType -eq "ServicePrincipal") {
            Invoke-GMMOperation -OperationName "Stop" -AuthMethod $resetGMMType `
                -SolutionAbbreviation $solutionAbbreviation `
                -EnvironmentAbbreviation $environmentAbbreviation
        } else {
            Write-DeployLog -Level Warn -Message "Skipping pre-deployment stop - resetGMMType is '$resetGMMType'."
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
        Write-DeployLog -Level Warn -Message "Skipping Web App configuration as per configuration [skipAppRegistrationSetup = $($parameterHashtable.skipAppRegistrationSetup.value)]."
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
        Write-DeployLog -Level Info -Message "Calling Reschedule endpoint via WebApi..."
        if ($resetGMMType -eq "Credentials" -or $resetGMMType -eq "ServicePrincipal") {
            Invoke-GMMOperation -OperationName "Reschedule" -AuthMethod $resetGMMType `
                -SolutionAbbreviation $solutionAbbreviation `
                -EnvironmentAbbreviation $environmentAbbreviation
        }
        Write-DeployLog -Level Info -Message "Starting all remaining function apps..."
        Start-FunctionApps -ResourceGroupName $computeResourceGroup
    } elseif ($startFunctions) {
        Start-FunctionApps -ResourceGroupName $computeResourceGroup
    }

    Write-DeployPhase -Name 'Deployment Complete' -Event Begin
    Write-DeployLog -Level Success -Message "Deployment complete!"
    Write-DeployResult -Status SUCCESS
    Write-DeployPhase -Name 'Deployment Complete' -Event End

    # Post-success informational output only. Guarded in its own try/catch so a
    # transient Azure read here cannot reach the outer catch and flip an
    # already-successful deployment to FAILED (the LAST result marker wins).
    try {
        if ($parameterHashtable.skipAppRegistrationSetup -ne $true -and $appRegistrationSetupResult -ne $null -and $appRegistrationSetupResult.AppsThatNeedAdminConsent.Count -gt 0) {
            Write-DeployLog -Level Warn -Message "The following applications might require admin consent:"

            foreach ($app in $appRegistrationSetupResult.AppsThatNeedAdminConsent) {
                $consentUrl = "https://portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationMenuBlade/~/CallAnAPI/appId/$($app.ApplicationId)"
                Write-DeployLog -Level Info -Message ("`n{0} - {1}" -f $app.ApplicationName, $consentUrl)
            }

            Write-DeployLog -Level Warn -Message "Please use the provided URLs to open the Azure portal and grant admin consent for these applications."
        }

        Write-Host ""
        Write-DeployLog -Level Success -Message "Access the GMM UI here:"


        $staticWebApp = Invoke-WithRetry `
            -Operation { Get-AzStaticWebApp -Name "$SolutionAbbreviation-ui" -ResourceGroupName $computeResourceGroup } `
            -OperationName "Get static web app URL" `
            -MaxAttempts 3 -BaseDelaySeconds 2
        if ($null -ne $staticWebApp) {
            Write-DeployLog -Level Success -Message "https://$($staticWebApp.DefaultHostname)`n"
        }
    }
    catch {
        Write-DeployLog -Level Warn -Message "Post-deployment info display failed; deployment already succeeded: $_"
    }

    } catch {
        Write-DeployResult -Status FAILED -Reason $_.Exception.Message -LastPhase $global:GmmCurrentDeployPhase
        throw
    } finally {
        if ($transcriptStarted) {
            Stop-Transcript
        }
    }
}
