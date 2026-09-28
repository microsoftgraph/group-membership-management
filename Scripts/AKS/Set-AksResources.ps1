function Set-AksPrereqResources {
    param (
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$SubscriptionId,
        [Parameter(Mandatory = $true)]
        [string]$AksTemplateDirectoryPath,
        [Parameter(Mandatory = $true)]
        [Hashtable]$ParameterHashtable,
        [Parameter(Mandatory = $false)]
        [Hashtable]$AdditionalParameters = @{ parameters = @{} }
    )

    Write-DeployPhase -Name 'Creating AKS Prerequisite Resources' -Event Begin
    $computeResourceGroup = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation"
    $templateFilePath = "$AksTemplateDirectoryPath/aksPrereqs.json"
    Invoke-WithRetry `
        -Operation {
            Start-ResourceDeployment `
                -ResourceGroupName $computeResourceGroup `
                -SubscriptionId $SubscriptionId `
                -TemplateFilePath $templateFilePath `
                -ParameterHashtable $ParameterHashtable `
                -AdditionalParameters $AdditionalParameters
        } `
        -OperationName "Create AKS prerequisite resources" `
        -MaxAttempts $maxRetriesForDeploymentOperations `
        -BaseDelaySeconds 2

    Write-DeployPhase -Name 'Creating AKS Prerequisite Resources' -Event End
}

function Wait-AksClusterProvisioningSettled {
    param (
        [Parameter(Mandatory = $true)]
        [string]$ClusterName,
        [Parameter(Mandatory = $true)]
        [string]$ClusterReadPath,
        [Parameter(Mandatory = $true)]
        [datetime]$Deadline
    )

    $inFlightStates = @('Starting', 'Stopping', 'Updating', 'Creating', 'Deleting')
    $lastProvisioningState = 'unknown'

    while ((Get-Date) -lt $Deadline) {
        Start-Sleep -Seconds 30
        $pollResponse = Invoke-AzRestMethod -Method GET -Path $ClusterReadPath
        if ($pollResponse.StatusCode -ne 200) {
            continue
        }

        $properties = ($pollResponse.Content | ConvertFrom-Json).properties
        $lastProvisioningState = $properties.provisioningState
        Write-DeployLog -Level Info -Message "AKS cluster '$ClusterName' provisioning state is '$lastProvisioningState', power state is '$($properties.powerState.code)'."

        if ($lastProvisioningState -notin $inFlightStates) {
            return $properties
        }
    }

    throw "AKS cluster '$ClusterName' was still in provisioning state '$lastProvisioningState' when the wait budget ran out. Another operation is in flight on this cluster, so starting or deploying it now would be rejected with a conflict. Wait for that operation to finish and re-run."
}

function Start-AksClusterIfStopped {
    param (
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$SubscriptionId,
        [Parameter(Mandatory = $false)]
        [int]$TimeoutMinutes = 20
    )

    Write-DeployPhase -Name 'Starting AKS Cluster' -Event Begin

    $computeResourceGroup = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation"
    $clusterName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-aks"
    $clusterResourceId = "/subscriptions/$SubscriptionId/resourceGroups/$computeResourceGroup/providers/Microsoft.ContainerService/managedClusters/$clusterName"
    $clusterReadPath = "$clusterResourceId`?api-version=2024-05-01"
    $clusterStartPath = "$clusterResourceId/start`?api-version=2024-05-01"

    $clusterResponse = Invoke-WithRetry `
        -Operation { Invoke-AzRestMethod -Method GET -Path $clusterReadPath } `
        -OperationName "Read AKS cluster power state" `
        -MaxAttempts 3 -BaseDelaySeconds 2

    if ($clusterResponse.StatusCode -eq 404) {
        Write-DeployLog -Level Info -Message "AKS cluster '$clusterName' does not exist yet. The cluster deployment below creates it."
        Write-DeployPhase -Name 'Starting AKS Cluster' -Event End
        return
    }

    if ($clusterResponse.StatusCode -ne 200) {
        throw "Could not read AKS cluster '$clusterName' in '$computeResourceGroup'. ARM returned $($clusterResponse.StatusCode): $($clusterResponse.Content)"
    }

    $properties = ($clusterResponse.Content | ConvertFrom-Json).properties
    $inFlightStates = @('Starting', 'Stopping', 'Updating', 'Creating', 'Deleting')
    $deadline = (Get-Date).AddMinutes($TimeoutMinutes)

    if ($properties.provisioningState -in $inFlightStates) {
        Write-DeployLog -Level Info -Message "AKS cluster '$clusterName' is in provisioning state '$($properties.provisioningState)'. Waiting for that operation to finish before reading its power state - starting a cluster mid-operation is rejected with a conflict."
        $properties = Wait-AksClusterProvisioningSettled -ClusterName $clusterName -ClusterReadPath $clusterReadPath -Deadline $deadline
    }

    $powerState = $properties.powerState.code
    if ($powerState -ne 'Stopped') {
        Write-DeployLog -Level Info -Message "AKS cluster '$clusterName' power state is '$powerState'. No action needed."
        Write-DeployPhase -Name 'Starting AKS Cluster' -Event End
        return
    }

    Write-DeployLog -Level Info -Message "AKS cluster '$clusterName' is stopped. Starting it before deploying the cluster template."
    $startResponse = Invoke-WithRetry `
        -Operation { Invoke-AzRestMethod -Method POST -Path $clusterStartPath } `
        -OperationName "Start AKS cluster" `
        -MaxAttempts 3 -BaseDelaySeconds 2

    if ($null -eq $startResponse -or $startResponse.StatusCode -notin @(200, 202)) {
        throw "Failed to start AKS cluster '$clusterName'. ARM returned $($startResponse.StatusCode): $($startResponse.Content)"
    }

    $properties = Wait-AksClusterProvisioningSettled -ClusterName $clusterName -ClusterReadPath $clusterReadPath -Deadline $deadline
    if ($properties.powerState.code -ne 'Running') {
        throw "AKS cluster '$clusterName' settled in provisioning state '$($properties.provisioningState)' with power state '$($properties.powerState.code)' instead of Running, so it is not usable for this deployment."
    }

    Write-DeployPhase -Name 'Starting AKS Cluster' -Event End
}

function Set-AksClusterResources {
    param (
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$SubscriptionId,
        [Parameter(Mandatory = $true)]
        [string]$AksTemplateDirectoryPath,
        [Parameter(Mandatory = $true)]
        [Hashtable]$ParameterHashtable,
        [Parameter(Mandatory = $false)]
        [Hashtable]$AdditionalParameters = @{ parameters = @{} }
    )

    Write-DeployPhase -Name 'Creating AKS Cluster' -Event Begin
    $computeResourceGroup = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation"
    $templateFilePath = "$AksTemplateDirectoryPath/aksResources.json"

    Start-AksClusterIfStopped `
        -SolutionAbbreviation           $SolutionAbbreviation `
        -EnvironmentAbbreviation        $EnvironmentAbbreviation `
        -SubscriptionId                 $SubscriptionId

    Invoke-WithRetry `
        -Operation {
            Start-ResourceDeployment `
                -ResourceGroupName $computeResourceGroup `
                -SubscriptionId $SubscriptionId `
                -TemplateFilePath $templateFilePath `
                -ParameterHashtable $ParameterHashtable `
                -AdditionalParameters $AdditionalParameters
        } `
        -OperationName "Create AKS resources" `
        -MaxAttempts $maxRetriesForDeploymentOperations `
        -BaseDelaySeconds 2

    Write-DeployPhase -Name 'Creating AKS Cluster' -Event End
}

function Set-AksResources {
    param (
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$SubscriptionId,
        [Parameter(Mandatory = $true)]
        [string]$AksTemplateDirectoryPath,
        [Parameter(Mandatory = $true)]
        [Hashtable]$ParameterHashtable,
        [Parameter(Mandatory = $false)]
        [Hashtable]$AdditionalParameters = @{ parameters = @{} },
        [Parameter(Mandatory = $false)]
        [string]$DeploymentPrincipalObjectId
    )

    Write-DeployLog -Level Info -Message "Creating AKS resources"

    Set-AksPrereqResources `
        -SolutionAbbreviation           $SolutionAbbreviation `
        -EnvironmentAbbreviation        $EnvironmentAbbreviation `
        -SubscriptionId                 $SubscriptionId `
        -AksTemplateDirectoryPath       $AksTemplateDirectoryPath `
        -ParameterHashtable             $ParameterHashtable `
        -AdditionalParameters           $AdditionalParameters

    Assert-AksRoleAssignments `
        -SolutionAbbreviation           $SolutionAbbreviation `
        -EnvironmentAbbreviation        $EnvironmentAbbreviation `
        -SubscriptionId                 $SubscriptionId `
        -DeploymentPrincipalObjectId    $DeploymentPrincipalObjectId

    Set-AksClusterResources `
        -SolutionAbbreviation           $SolutionAbbreviation `
        -EnvironmentAbbreviation        $EnvironmentAbbreviation `
        -SubscriptionId                 $SubscriptionId `
        -AksTemplateDirectoryPath       $AksTemplateDirectoryPath `
        -ParameterHashtable             $ParameterHashtable `
        -AdditionalParameters           $AdditionalParameters
}

function Get-RoleAssignmentsForPrincipal {
    param (
        [Parameter(Mandatory = $true)]
        [string]$Scope,
        [Parameter(Mandatory = $true)]
        [string]$PrincipalId
    )

    $filter = [System.Uri]::EscapeDataString("principalId eq '$PrincipalId'")
    $uri = "https://management.azure.com$Scope/providers/Microsoft.Authorization/roleAssignments?api-version=2022-04-01&`$filter=$filter"
    $token = Get-BearerToken
    $headers = @{
        Authorization  = "Bearer $token"
        'Content-Type' = 'application/json'
    }

    $response = Invoke-RestMethod -Uri $uri -Method Get -Headers $headers

    return @($response.value | Where-Object {
        $assignmentScope = $_.properties.scope
        -not [string]::IsNullOrWhiteSpace($assignmentScope) -and
        (
            $Scope.Equals($assignmentScope, [System.StringComparison]::OrdinalIgnoreCase) -or
            $Scope.StartsWith("$assignmentScope/", [System.StringComparison]::OrdinalIgnoreCase)
        )
    })
}

function Test-RoleAssignmentExists {
    param (
        [Parameter(Mandatory = $true)]
        [string]$Scope,
        [Parameter(Mandatory = $true)]
        [string]$PrincipalId,
        [Parameter(Mandatory = $true)]
        [string]$RoleDefinitionId
    )

    $assignments = Get-RoleAssignmentsForPrincipal -Scope $Scope -PrincipalId $PrincipalId
    foreach ($assignment in $assignments) {
        if ($assignment.properties.roleDefinitionId -like "*/$RoleDefinitionId") {
            return $true
        }
    }

    return $false
}

function Assert-AksRoleAssignments {
    param (
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$SubscriptionId,
        [Parameter(Mandatory = $false)]
        [string]$DeploymentPrincipalObjectId
    )

    Write-DeployPhase -Name 'Verifying AKS Role Assignments' -Event Begin

    $computeResourceGroup = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation"

    # These names duplicate the convention aksPrereqs.bicep and aksResources.bicep
    # build their resource names from. A naming change in either template must be
    # made here too, or the preflight inspects the wrong resource.
    $clusterName = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation-aks"
    $names = @{
        ClusterName              = $clusterName
        VnetName                 = "$clusterName-vnet"
        NodeSubnetName           = 'aks-nodes'
        ControlPlaneIdentityName = "$clusterName-identity"
        KubeletIdentityName      = "$clusterName-kubelet-identity"
        OutboundPipName          = "$clusterName-outbound-pip"
        AcrName                  = "$SolutionAbbreviation`computeacr$EnvironmentAbbreviation"
    }

    $roleAssignmentScript = ". Scripts/AKS/Set-AksRoleAssignments.ps1; Set-AksRoleAssignments -SolutionAbbreviation $SolutionAbbreviation -EnvironmentAbbreviation $EnvironmentAbbreviation"
    $resourceGroupScope = "/subscriptions/$SubscriptionId/resourceGroups/$computeResourceGroup"

    $networkContributorRoleId     = '4d97b98b-1d4f-4787-a291-c67834d212e7'
    $managedIdentityOperatorRole  = 'f1a07417-d97a-45cb-824c-7a7467783830'
    $acrPullRoleId                = '7f951dda-4ed3-4680-a7ca-43fe172d538d'
    $clusterAdminRoleId           = 'b1ff04bb-8a4e-4dc4-8eb5-8693973ce19b'

    $controlPlaneIdentityId = "$resourceGroupScope/providers/Microsoft.ManagedIdentity/userAssignedIdentities/$($names.ControlPlaneIdentityName)"
    $kubeletIdentityId      = "$resourceGroupScope/providers/Microsoft.ManagedIdentity/userAssignedIdentities/$($names.KubeletIdentityName)"
    $nodeSubnetId           = "$resourceGroupScope/providers/Microsoft.Network/virtualNetworks/$($names.VnetName)/subnets/$($names.NodeSubnetName)"
    $outboundPipId          = "$resourceGroupScope/providers/Microsoft.Network/publicIPAddresses/$($names.OutboundPipName)"
    $acrId                  = "$resourceGroupScope/providers/Microsoft.ContainerRegistry/registries/$($names.AcrName)"

    $identityUri = "https://management.azure.com$($controlPlaneIdentityId)?api-version=2023-01-31"
    $token = Get-BearerToken
    $headers = @{
        Authorization  = "Bearer $token"
        'Content-Type' = 'application/json'
    }

    $controlPlanePrincipalId = $null
    try {
        $identity = Invoke-RestMethod -Uri $identityUri -Method Get -Headers $headers
        $controlPlanePrincipalId = $identity.properties.principalId
    }
    catch {
        throw @"
AKS preflight failed: the control-plane identity '$($names.ControlPlaneIdentityName)' was not found in resource group '$computeResourceGroup'.

This means the AKS prerequisite deployment has not completed. It runs immediately before this check in the same stage, so a failure here indicates that earlier deployment did not succeed - review the 'Creating AKS Prerequisite Resources' phase above for the real error.

Underlying error: $($_.Exception.Message)
"@
    }

    if ([string]::IsNullOrWhiteSpace($controlPlanePrincipalId)) {
        throw "AKS preflight failed: the control-plane identity '$($names.ControlPlaneIdentityName)' exists but has no principalId. Re-run the AKS prerequisite deployment."
    }

    Write-DeployLog -Level Info -Message "Control-plane identity '$($names.ControlPlaneIdentityName)' resolved to principal $controlPlanePrincipalId."

    $kubeletPrincipalId = $null
    try {
        $kubeletIdentity = Invoke-RestMethod -Uri "https://management.azure.com$($kubeletIdentityId)?api-version=2023-01-31" -Method Get -Headers $headers
        $kubeletPrincipalId = $kubeletIdentity.properties.principalId
    }
    catch {
        throw @"
AKS preflight failed: the kubelet identity '$($names.KubeletIdentityName)' was not found in resource group '$computeResourceGroup'.

This means the AKS prerequisite deployment has not completed. It runs immediately before this check in the same stage, so a failure here indicates that earlier deployment did not succeed - review the 'Creating AKS Prerequisite Resources' phase above for the real error.

Underlying error: $($_.Exception.Message)
"@
    }

    if ([string]::IsNullOrWhiteSpace($kubeletPrincipalId)) {
        throw "AKS preflight failed: the kubelet identity '$($names.KubeletIdentityName)' exists but has no principalId. Re-run the AKS prerequisite deployment."
    }

    Write-DeployLog -Level Info -Message "Kubelet identity '$($names.KubeletIdentityName)' resolved to principal $kubeletPrincipalId."

    $serviceConnectionName = "$SolutionAbbreviation-serviceconnection-$EnvironmentAbbreviation"
    if ([string]::IsNullOrWhiteSpace($DeploymentPrincipalObjectId)) {
        Write-DeployLog -Level Warn -Message "No DeploymentPrincipalObjectId was supplied, so the Kubernetes cluster-admin grant for '$serviceConnectionName' is NOT checked. Pass the service connection's object id to have it verified here. If that grant is missing, the deployment fails later at 'helm upgrade' with 'secrets is forbidden'."
    }
    else {
        Write-DeployLog -Level Info -Message "Checking the Kubernetes cluster-admin grant for '$serviceConnectionName' (principal $DeploymentPrincipalObjectId)."
    }

    $requiredGrants = @(
        @{ Description = "Network Contributor on the node subnet '$($names.VnetName)/$($names.NodeSubnetName)'"; PrincipalId = $controlPlanePrincipalId; Scope = $nodeSubnetId;      RoleDefinitionId = $networkContributorRoleId }
        @{ Description = "Network Contributor on the outbound public IP '$($names.OutboundPipName)'";            PrincipalId = $controlPlanePrincipalId; Scope = $outboundPipId;     RoleDefinitionId = $networkContributorRoleId }
        @{ Description = "Managed Identity Operator on the kubelet identity '$($names.KubeletIdentityName)'";    PrincipalId = $controlPlanePrincipalId; Scope = $kubeletIdentityId; RoleDefinitionId = $managedIdentityOperatorRole }
        @{ Description = "AcrPull for the kubelet identity '$($names.KubeletIdentityName)' on the registry '$($names.AcrName)'"; PrincipalId = $kubeletPrincipalId; Scope = $acrId; RoleDefinitionId = $acrPullRoleId }
    )

    if (-not [string]::IsNullOrWhiteSpace($DeploymentPrincipalObjectId)) {
        $requiredGrants += @{ Description = "Azure Kubernetes Service RBAC Cluster Admin for the deployment service principal '$serviceConnectionName' on resource group '$computeResourceGroup'"; PrincipalId = $DeploymentPrincipalObjectId; Scope = $resourceGroupScope; RoleDefinitionId = $clusterAdminRoleId }
    }

    $missingGrants = @()
    foreach ($grant in $requiredGrants) {
        $grantScope = $grant.Scope
        $grantRoleId = $grant.RoleDefinitionId
        $grantPrincipalId = $grant.PrincipalId
        $exists = Invoke-WithRetry `
            -Operation {
                Test-RoleAssignmentExists `
                    -Scope $grantScope `
                    -PrincipalId $grantPrincipalId `
                    -RoleDefinitionId $grantRoleId
            } `
            -OperationName "Check role assignment: $($grant.Description)" `
            -MaxAttempts 3 `
            -BaseDelaySeconds 2

        if ($exists) {
            Write-DeployLog -Level Info -Message "Present: $($grant.Description)."
        }
        else {
            Write-DeployLog -Level Warn -Message "MISSING: $($grant.Description)."
            $missingGrants += $grant
        }
    }

    if ($missingGrants.Count -gt 0) {
        $missingList = ($missingGrants | ForEach-Object { "  - $($_.Description)`n      scope: $($_.Scope)" }) -join "`n"
        throw @"
AKS cluster deployment BLOCKED - required role assignments are missing.

Azure validates these grants when the cluster is created, so deploying the cluster now would fail with an ARM error that does not name the cause. The following $($missingGrants.Count) grant(s) required by the AKS deployment are missing:

$missingList

HOW TO FIX

  1. Have an authorized operator run:

         $roleAssignmentScript

     This creates every role assignment the AKS workload needs, in a single pass.

  2. It requires PIM elevation on a privileged workstation, because creating a
     role assignment needs Microsoft.Authorization/roleAssignments/write. The
     deployment identity deliberately does NOT hold that permission, so THE
     PIPELINE CANNOT DO THIS STEP AND WILL NOT ATTEMPT IT. Granting the
     deployment identity that permission is not an acceptable workaround.

  3. Re-run this pipeline once the script has completed. It only has to be run
     once per environment; every later deployment finds the grants in place and
     continues straight through.

The AKS prerequisite deployment that runs before this check has already
succeeded, so re-running costs nothing and repeats no work.
"@
    }

    Write-DeployLog -Level Info -Message "All required AKS role assignments are present."
    Write-DeployPhase -Name 'Verifying AKS Role Assignments' -Event End
}
