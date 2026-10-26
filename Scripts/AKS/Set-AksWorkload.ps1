function Set-AksWorkload {
    param (
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$DeploymentPackagePath,
        [Parameter(Mandatory = $true)]
        [object[]]$Services,
        [Parameter(Mandatory = $false)]
        [string]$NamespaceName = 'gmm'
    )

    Write-DeployPhase -Name 'Deploying AKS Workload' -Event Begin

    $scriptsDirectory = Split-Path $PSScriptRoot -Parent
    . ($scriptsDirectory + '/ReusableModules/Invoke-WithFirewallRetry.ps1')

    $imageTag = Get-Date -Format 'yyyyMMddHHmmss'

    $computeResourceGroup = "$SolutionAbbreviation-compute-$EnvironmentAbbreviation"
    $acrName = "$SolutionAbbreviation`computeacr$EnvironmentAbbreviation"

    $connection = Connect-AksCluster `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation

    if (-not $connection.Available) {
        throw "Cannot deploy the AKS workloads: $($connection.Reason)"
    }

    $tenantId = $connection.TenantId

    # The Container Insights agent reads this from kube-system. Without it the agent
    # keeps only non-Normal events, so pod kills and autoscaler rescales go
    # unrecorded. env_var stays off: the pods load connection strings and passwords
    # into their environment from Key Vault.
    $containerInsightsAgentConfigMap = @'
kind: ConfigMap
apiVersion: v1
metadata:
  name: container-azm-ms-agentconfig
  namespace: kube-system
data:
  schema-version: v1
  config-version: ver1
  log-data-collection-settings: |-
    [log_collection_settings]
       [log_collection_settings.stdout]
          enabled = true
          exclude_namespaces = ["kube-system","gatekeeper-system","kube-public","kube-node-lease"]
       [log_collection_settings.stderr]
          enabled = true
          exclude_namespaces = ["kube-system","gatekeeper-system","kube-public","kube-node-lease"]
       [log_collection_settings.env_var]
          enabled = false
       [log_collection_settings.collect_all_kube_events]
          enabled = true
       [log_collection_settings.schema]
          containerlog_schema_version = "v2"
'@
    $containerInsightsAgentConfigMap | kubectl apply -f -
    if ($LASTEXITCODE -ne 0) { throw "kubectl apply failed for the Container Insights agent ConfigMap." }
    Write-DeployLog -Level Success -Message "Applied the Container Insights agent ConfigMap."

    foreach ($service in $Services) {
        Set-AksServiceWorkload `
            -SolutionAbbreviation    $SolutionAbbreviation `
            -EnvironmentAbbreviation $EnvironmentAbbreviation `
            -ServiceName             $service.Name `
            -Autoscaled              $service.Autoscaled `
            -DeploymentPackagePath   $DeploymentPackagePath `
            -AcrName                 $acrName `
            -ComputeResourceGroup    $computeResourceGroup `
            -TenantId                $tenantId `
            -ImageTag                $imageTag `
            -NamespaceName           $NamespaceName
    }

    Write-DeployPhase -Name 'Deploying AKS Workload' -Event End
}

function Set-AksServiceWorkload {
    param (
        [Parameter(Mandatory = $true)][string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)][string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)][string]$ServiceName,
        [Parameter(Mandatory = $true)][bool]$Autoscaled,
        [Parameter(Mandatory = $true)][string]$DeploymentPackagePath,
        [Parameter(Mandatory = $true)][string]$AcrName,
        [Parameter(Mandatory = $true)][string]$ComputeResourceGroup,
        [Parameter(Mandatory = $true)][string]$TenantId,
        [Parameter(Mandatory = $true)][string]$ImageTag,
        [Parameter(Mandatory = $true)][string]$NamespaceName
    )

    $chartName = $ServiceName.ToLower()
    $repository = "gmm/$chartName"
    $chartPath = Join-Path $DeploymentPackagePath "AKS/charts/$chartName"
    $identityName = "$SolutionAbbreviation-identity-$EnvironmentAbbreviation-aks-$ServiceName"
    $autoscalingArguments = @()
    if ($Autoscaled) {
        $autoscalingArguments = @(Resolve-AksAutoscalingArguments `
            -ChartPath $chartPath `
            -SolutionAbbreviation $SolutionAbbreviation `
            -EnvironmentAbbreviation $EnvironmentAbbreviation)
    }

    $zip = Join-Path (Split-Path $DeploymentPackagePath -Parent) "function_packages/$ServiceName.zip"
    if (-not (Test-Path $zip)) {
        throw "Function package not found: $zip. The AKS workload is assembled from the same package the function app is deployed from."
    }

    $context = Join-Path ([System.IO.Path]::GetTempPath()) "gmm-imgctx-$ServiceName-$ImageTag-$(Get-Random -Minimum 100000 -Maximum 999999)"
    New-Item -ItemType Directory -Path $context | Out-Null
    try {
        Expand-Archive -Path $zip -DestinationPath $context -Force

        foreach ($required in @('host.json', 'worker.config.json', 'functions.metadata')) {
            if (-not (Test-Path (Join-Path $context $required))) {
                throw "Function package '$zip' is missing $required. An image built from it would start cleanly and index zero functions."
            }
        }

        $dockerfile = Join-Path $DeploymentPackagePath "AKS/docker/gmm-service/Dockerfile"
        if (-not (Test-Path $dockerfile)) {
            throw "Dockerfile not found: $dockerfile. Every AKS service image is built from this one file."
        }
        Copy-Item $dockerfile $context -Force

        Write-DeployLog -Level Info -Message "Assembling $AcrName.azurecr.io/$repository`:$ImageTag"
        az acr build --registry $AcrName --image "$repository`:$ImageTag" --file "$context/Dockerfile" --platform linux $context
        if ($LASTEXITCODE -ne 0) { throw "az acr build failed for $ServiceName." }
    }
    finally {
        Remove-Item -LiteralPath $context -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $context) {
            Write-DeployLog -Level Warn -Message "Could not remove the image build context '$context'. It holds an expanded copy of $zip and can be deleted by hand; the deployment continues."
        }
    }

    $manifestJson = Invoke-WithRetry `
        -Operation {
            Invoke-AzCli -OperationName "Resolve image digest for $ServiceName" -Arguments @(
                'acr','manifest','show-metadata','--registry',$AcrName,
                '--name',"$repository`:$ImageTag",'--output','json','--only-show-errors')
        } `
        -OperationName "Resolve image digest for $ServiceName" `
        -MaxAttempts 3 -BaseDelaySeconds 2

    $manifest = $manifestJson | ConvertFrom-Json
    $digest = $manifest.digest
    if (-not $digest) { throw "Could not resolve the image digest for $repository`:$ImageTag in $AcrName." }
    Write-DeployLog -Level Info -Message "digest = $digest"

    # The scheduled purge must not delete the image currently deployed, or a node
    # that restarts later cannot pull it. Superseded digests are unlocked at step 6.
    Invoke-WithRetry `
        -Operation {
            Invoke-AzCli -OperationName "Lock image digest for $ServiceName" -Arguments @(
                'acr','manifest','update-metadata','--registry',$AcrName,
                '--name',"$repository@$digest",'--delete-enabled','false',
                '--write-enabled','false','--output','none','--only-show-errors') | Out-Null
        } `
        -OperationName "Lock image digest for $ServiceName" `
        -MaxAttempts 3 -BaseDelaySeconds 2
    Write-DeployLog -Level Success -Message "locked $repository@$digest against purge"

    $workloadClientId = Invoke-AzCli -OperationName "Resolve the managed identity '$identityName' in '$ComputeResourceGroup'" -Arguments @(
        'identity','show','--resource-group',$ComputeResourceGroup,'--name',$identityName,
        '--query','clientId','--output','tsv','--only-show-errors')
    if (-not $workloadClientId) { throw "Managed identity '$identityName' in '$ComputeResourceGroup' has no client id." }

    $dataKeyVaultName = "$SolutionAbbreviation-data-$EnvironmentAbbreviation"
    $storageAccountName = Invoke-WithFirewallRetry `
        -ResourceGroup $dataKeyVaultName `
        -Operation {
            Invoke-AzCli -OperationName "Read the secret 'functionsStorageAccountName' from key vault '$dataKeyVaultName'" -Arguments @(
                'keyvault','secret','show','--vault-name',$dataKeyVaultName,
                '--name','functionsStorageAccountName','--query','value','--output','tsv','--only-show-errors')
        } `
        -OnFirewallError {
            param($errorMessage)
            if ($errorMessage -match '\b((25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3})\b') {
                $blockedAddress = $Matches[1]
                Write-DeployLog -Level Warn -Message "Key vault '$dataKeyVaultName' rejected $blockedAddress. Adding it to the vault's network rules and retrying."
                Invoke-AzCli -OperationName "Add $blockedAddress to the network rules of key vault '$dataKeyVaultName'" -Arguments @(
                    'keyvault','network-rule','add','--name',$dataKeyVaultName,
                    '--resource-group',$dataKeyVaultName,'--ip-address',$blockedAddress,
                    '--output','none','--only-show-errors') | Out-Null
                Start-Sleep -Seconds 10
            }
        }
    if ([string]::IsNullOrWhiteSpace($storageAccountName)) { throw "Secret 'functionsStorageAccountName' in key vault '$dataKeyVaultName' is missing, empty, or not readable. The pod must share the function app's storage account, so the deployment cannot continue without it." }

    $appConfigEndpoint = "https://$SolutionAbbreviation-appconfig-$EnvironmentAbbreviation.azconfig.io"

    Write-DeployLog -Level Info -Message "--- resolved deploy-time values ---"
    Write-DeployLog -Level Info -Message "  workloadIdentity.clientId       : $workloadClientId"
    Write-DeployLog -Level Info -Message "  keyVault.tenantId               : $TenantId"
    Write-DeployLog -Level Info -Message "  storage.accountName             : $storageAccountName"
    Write-DeployLog -Level Info -Message "  config.appConfigurationEndpoint : $appConfigEndpoint"

    if (-not (Test-Path $chartPath)) { throw "Chart not found: $chartPath" }
    if (-not $Autoscaled) {
        helm dependency build $chartPath
        if ($LASTEXITCODE -ne 0) { throw "helm dependency build failed for $chartName." }
    }

    Write-DeployLog -Level Info -Message "replicaCount = 0 - the deploy installs the artifact; the WebApi Start activates it."

    $existingJson = kubectl get deployment $chartName -n $NamespaceName -o json 2>$null
    if ($LASTEXITCODE -eq 0 -and $existingJson) {
        $existing = $existingJson | ConvertFrom-Json
        $foreign = $existing.metadata.managedFields | Where-Object { $_.manager -ne 'helm' -and $_.manager -ne 'kube-controller-manager' }
        if ($foreign) {
            Write-DeployLog -Level Warn -Message "Out-of-band field managers present on deployment/$chartName - this deploy will RECLAIM their fields:"
            foreach ($manager in $foreign) {
                Write-DeployLog -Level Warn -Message "  manager '$($manager.manager)' (operation $($manager.operation), updated $($manager.time))"
            }
            Write-DeployLog -Level Warn -Message "Change this workload through the deployment, not with kubectl or portal edits."
        }
    }

    # --force-conflicts is required. Helm applies server-side, so anything that
    # touches the live object outside Helm - kubectl scale/set/edit, a portal edit -
    # takes ownership of those fields and the next deploy fails with a conflict.
    # The upgrade AND its automatic rollback both fail, leaving the release failed
    # and further deploys impossible until a human intervenes; clearing
    # managedFields makes it worse.
    $timeout = if ($Autoscaled) { '900s' } else { '10m' }
    helm upgrade --install $chartName $chartPath `
        --namespace $NamespaceName --create-namespace `
        --set image.repository="$AcrName.azurecr.io/$repository" `
        --set image.digest="$digest" `
        --set replicaCount=0 `
        @autoscalingArguments `
        --set workloadIdentity.clientId="$workloadClientId" `
        --set storage.accountName="$storageAccountName" `
        --set config.appConfigurationEndpoint="$appConfigEndpoint" `
        --set keyVault.tenantId="$TenantId" `
        --set keyVault.names.prereqs="$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation" `
        --set keyVault.names.data="$SolutionAbbreviation-data-$EnvironmentAbbreviation" `
        --force-conflicts `
        --history-max 3 `
        --wait --timeout $timeout --rollback-on-failure
    if ($LASTEXITCODE -ne 0) { throw "helm upgrade --install failed for $chartName." }

    Write-DeployLog -Level Success -Message "$chartName deployed at replicaCount=0 - not dispatching until it is started."

    # After the deploy succeeds, so a failed one never unlocks the running image.
    Update-AksImageLockWindow `
        -AcrName       $AcrName `
        -Repository    $repository `
        -ReleaseName   $chartName `
        -NamespaceName $NamespaceName
}

function Resolve-AksAutoscalingArguments {
    <#
    .SYNOPSIS
    Prepares an autoscaled chart and returns the Helm arguments its descriptor requires.

    .DESCRIPTION
    Checks the KEDA CRDs, builds the chart dependencies, reads the Key Vault secrets the
    chart's autoscaling descriptor lists, and returns them as --set-string arguments
    together with the paused-at-zero setting.
    #>
    param(
        [Parameter(Mandatory)][string]$ChartPath,
        [Parameter(Mandatory)][string]$SolutionAbbreviation,
        [Parameter(Mandatory)][string]$EnvironmentAbbreviation
    )

    $crds = kubectl get crd scaledobjects.keda.sh triggerauthentications.keda.sh -o json --request-timeout=30s
    if ($LASTEXITCODE -ne 0 -or @(($crds -join "`n" | ConvertFrom-Json).items).Count -ne 2) {
        throw 'Both mandatory KEDA CRDs must be installed before an autoscaled workload is deployed (state UNKNOWN if kubectl failed).'
    }
    if (-not (Test-Path $ChartPath)) { throw "Chart not found: $ChartPath" }
    helm dependency build $ChartPath --skip-refresh | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "helm dependency build failed for $(Split-Path $ChartPath -Leaf)." }

    $descriptor = Get-AksAutoscalingDescriptor -ChartPath $ChartPath

    $valueArguments = @()
    foreach ($mapping in @($descriptor.helmValues)) {
        $source = $mapping.source
        $vault = "$SolutionAbbreviation-$($source.vault)-$EnvironmentAbbreviation"
        $vaultNameForRead = $vault
        $secretNameForRead = $source.name
        $value = Invoke-WithFirewallRetry `
            -ResourceGroup $vault `
            -Operation {
                Invoke-AzCli -OperationName "Read autoscaling metadata $secretNameForRead" -Arguments @(
                    'keyvault', 'secret', 'show', '--vault-name', $vaultNameForRead, '--name', $secretNameForRead,
                    '--query', 'value', '-o', 'tsv', '--only-show-errors')
            } `
            -OnFirewallError {
                param($errorMessage)
                if ($errorMessage -match '\b((25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3})\b') {
                    $blockedAddress = $Matches[1]
                    Invoke-AzCli -OperationName "Add $blockedAddress to the network rules of key vault '$vaultNameForRead'" -Arguments @(
                        'keyvault', 'network-rule', 'add', '--name', $vaultNameForRead, '--resource-group', $vaultNameForRead,
                        '--ip-address', $blockedAddress, '--output', 'none', '--only-show-errors') | Out-Null
                    Start-Sleep -Seconds 10
                }
            }

        if ([string]::IsNullOrWhiteSpace([string]$value)) {
            throw "Autoscaling descriptor value '$($mapping.path)' resolved to an empty value."
        }
        $valueArguments += @('--set-string', "$($mapping.path)=$([string]$value)")
    }
    return @('--set', 'autoscaling.pausedReplicas=0') + $valueArguments
}

function Get-AksAutoscalingDescriptor {
    <#
    .SYNOPSIS
    Reads the minimal chart-owned autoscaling deployment contract.

    .DESCRIPTION
    The chart-local schema is intentionally limited:
      helmValues[]: { path, source }
    A source is { type: keyVaultSecret, vault: data|prereqs, name }.

    .PARAMETER ChartPath
    The autoscaled service chart directory.
    #>
    param(
        [Parameter(Mandatory)][string]$ChartPath
    )

    $descriptorPath = Join-Path $ChartPath 'autoscaling-deployment.json'
    # The chart must ship an autoscaling descriptor
    if (-not (Test-Path -LiteralPath $descriptorPath)) {
        throw "Autoscaled chart '$ChartPath' is missing autoscaling-deployment.json."
    }

    # The descriptor must be valid JSON
    try {
        $descriptor = Get-Content -LiteralPath $descriptorPath -Raw | ConvertFrom-Json -AsHashtable
    }
    catch {
        throw "Autoscaling descriptor '$descriptorPath' is not valid JSON: $($_.Exception.Message)"
    }

    $valuePaths = @{}
    foreach ($mapping in @($descriptor.helmValues)) {
        # Each mapping needs a well-formed dotted Helm value path, such as autoscaling.topicName
        if ($mapping -isnot [System.Collections.IDictionary] -or
            [string]::IsNullOrWhiteSpace($mapping.path) -or
            $mapping.path -notmatch '^[A-Za-z][A-Za-z0-9]*(\.[A-Za-z][A-Za-z0-9]*)+$') {
            throw "Autoscaling descriptor '$descriptorPath' has an invalid Helm value mapping."
        }
        # A Helm value can be mapped only once
        if ($valuePaths.Keys -contains $mapping.path) {
            throw "Autoscaling descriptor '$descriptorPath' maps Helm value '$($mapping.path)' more than once."
        }
        $valuePaths[$mapping.path] = $true

        # Each mapping needs a source object
        $source = $mapping.source
        if ($source -isnot [System.Collections.IDictionary]) {
            throw "Autoscaling descriptor '$descriptorPath' mapping '$($mapping.path)' has no source."
        }
        switch ($source.type) {
            'keyVaultSecret' {
                # Key Vault sources must name a known vault (data or prereqs) and a well-formed secret name
                if ($source.vault -notin @('data', 'prereqs') -or
                    [string]::IsNullOrWhiteSpace($source.name) -or
                    $source.name -notmatch '^[A-Za-z0-9-]+$') {
                    throw "Autoscaling descriptor '$descriptorPath' mapping '$($mapping.path)' has an invalid Key Vault source."
                }
            }
            # Only keyVaultSecret sources are supported
            default {
                throw "Autoscaling descriptor '$descriptorPath' mapping '$($mapping.path)' uses unsupported source type '$($source.type)'."
            }
        }
    }

    # The descriptor must map at least one Helm value
    if ($valuePaths.Count -eq 0) {
        throw "Autoscaling descriptor '$descriptorPath' must map at least one Helm value."
    }

    return $descriptor
}

function Update-AksImageLockWindow {
    param (
        [Parameter(Mandatory = $true)][string]$AcrName,
        [Parameter(Mandatory = $true)][string]$Repository,
        [Parameter(Mandatory = $true)][string]$ReleaseName,
        [Parameter(Mandatory = $true)][string]$NamespaceName,
        [Parameter(Mandatory = $false)][int]$KeepLockedCount = 3
    )

    $liveImage = kubectl get deployment --namespace $NamespaceName `
        --selector "app.kubernetes.io/instance=$ReleaseName" `
        --output "jsonpath={.items[*].spec.template.spec.containers[*].image}"
    if ($LASTEXITCODE -ne 0 -or -not $liveImage) {
        Write-DeployLog -Level Warn -Message "Could not read the running image for '$ReleaseName' in namespace '$NamespaceName'. The live digest is UNKNOWN, not absent, so no lock is released this run."
        return
    }

    $liveDigest = if ($liveImage -match '@(sha256:[0-9a-f]+)') { $Matches[1] } else { $null }

    $manifestsJson = Invoke-AzCli -OperationName "List manifests for $Repository" -Arguments @(
        'acr','manifest','list-metadata','--registry',$AcrName,'--name',$Repository,
        '--orderby','time_desc','--query','[].{digest:digest,deleteEnabled:changeableAttributes.deleteEnabled}',
        '--output','json','--only-show-errors')
    $manifests = @($manifestsJson | ConvertFrom-Json)

    $superseded = @(
        $manifests |
            Select-Object -Skip $KeepLockedCount |
            Where-Object { $_.deleteEnabled -eq $false -and $_.digest -ne $liveDigest }
    )

    if ($superseded.Count -eq 0) {
        Write-DeployLog -Level Info -Message "Lock window: $($manifests.Count) manifest(s) in $Repository, none to release."
        return
    }

    Write-DeployLog -Level Info -Message "Lock window: releasing $($superseded.Count) superseded manifest(s) in $Repository."

    $released = 0
    foreach ($old in $superseded) {
        try {
            Invoke-AzCli -OperationName "Release lock on $($old.digest)" -Arguments @(
                'acr','manifest','update-metadata','--registry',$AcrName,
                '--name',"$Repository@$($old.digest)",'--delete-enabled','true',
                '--write-enabled','true','--output','none','--only-show-errors') | Out-Null
            $released++
        }
        catch {
            Write-DeployLog -Level Warn -Message "Could not release the lock on $Repository@$($old.digest); it stays protected. $_"
        }
    }

    Write-DeployLog -Level Success -Message "Lock window: newest $KeepLockedCount of $($manifests.Count) manifest(s) locked, $released released for purge."
}
