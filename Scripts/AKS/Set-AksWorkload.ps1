function Set-AksWorkload {
    param (
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$DeploymentPackagePath,
        [Parameter(Mandatory = $false)]
        [string[]]$Services = @('JobTrigger'),
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

    foreach ($service in $Services) {
        Set-AksServiceWorkload `
            -SolutionAbbreviation    $SolutionAbbreviation `
            -EnvironmentAbbreviation $EnvironmentAbbreviation `
            -ServiceName             $service `
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

    $identityName = "$SolutionAbbreviation-identity-$EnvironmentAbbreviation-aks-$ServiceName"
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
    helm dependency build $chartPath
    if ($LASTEXITCODE -ne 0) { throw "helm dependency build failed for $chartName." }

    Write-DeployLog -Level Info -Message "replicaCount = 0 - the deploy installs the artifact; Start-AksWorkloads activates it."

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
    helm upgrade --install $chartName $chartPath `
        --namespace $NamespaceName --create-namespace `
        --set image.repository="$AcrName.azurecr.io/$repository" `
        --set image.digest="$digest" `
        --set replicaCount=0 `
        --set workloadIdentity.clientId="$workloadClientId" `
        --set storage.accountName="$storageAccountName" `
        --set config.appConfigurationEndpoint="$appConfigEndpoint" `
        --set keyVault.tenantId="$TenantId" `
        --set keyVault.names.prereqs="$SolutionAbbreviation-prereqs-$EnvironmentAbbreviation" `
        --set keyVault.names.data="$SolutionAbbreviation-data-$EnvironmentAbbreviation" `
        --force-conflicts `
        --history-max 3 `
        --wait --timeout 10m --rollback-on-failure
    if ($LASTEXITCODE -ne 0) { throw "helm upgrade --install failed for $chartName." }

    Write-DeployLog -Level Success -Message "$chartName deployed at replicaCount=0 - not dispatching until it is started."

    # After the deploy succeeds, so a failed one never unlocks the running image.
    Update-AksImageLockWindow `
        -AcrName       $AcrName `
        -Repository    $repository `
        -ReleaseName   $chartName `
        -NamespaceName $NamespaceName
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
