function Stop-AksWorkloads {
    param (
        [Parameter(Mandatory = $true)]
        [string]$SolutionAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentAbbreviation,
        [Parameter(Mandatory = $true)]
        [string]$DeploymentPackagePath,
        [Parameter(Mandatory = $false)]
        [string]$NamespaceName = 'gmm'
    )

    Write-DeployPhase -Name 'Stopping AKS Workloads' -Event Begin

    $connection = Connect-AksCluster `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation

    if (-not $connection.Available) {
        Write-DeployLog -Level Warn -Message "Nothing to stop in AKS: $($connection.Reason) On the first deployment after deployAks is set to true this is expected, because the cluster is created later in the same deployment. Otherwise, investigate why it is unavailable."
        Write-DeployPhase -Name 'Stopping AKS Workloads' -Event End
        return
    }

    $releasesJson = helm list --namespace $NamespaceName --output json
    if ($LASTEXITCODE -ne 0) { throw "helm list failed for namespace '$NamespaceName' on cluster '$($connection.ClusterName)'." }

    $releases = @()
    if ($releasesJson) { $releases = @($releasesJson | ConvertFrom-Json) }
    if ($releases.Count -eq 0) {
        Write-DeployLog -Level Info -Message "No Helm releases in namespace '$NamespaceName' - nothing to stop."
        Write-DeployPhase -Name 'Stopping AKS Workloads' -Event End
        return
    }

    foreach ($release in $releases) {
        $releaseName = $release.name
        $chartPath = Join-Path $DeploymentPackagePath "AKS/charts/$releaseName"
        if (-not (Test-Path $chartPath)) {
            throw @"
Helm release '$releaseName' is deployed in namespace '$NamespaceName' but its chart was not found at '$chartPath', so it cannot be scaled down. Leaving it running would let it keep dispatching through the deployment, so this stops here rather than continuing.

Charts are resolved by convention as AKS/charts/<releaseName> in the deployment package, so this means the release was not installed from this package - for example an older service name, an operator-installed add-on, or a hand-installed test release.

Resolve it in one of two ways:
  - if the release does not belong here, remove it: helm uninstall $releaseName --namespace $NamespaceName
  - if it does, ship its chart in the deployment package as AKS/charts/$releaseName so every deployment can stand it down.
"@
        }

        helm dependency build $chartPath
        if ($LASTEXITCODE -ne 0) { throw "helm dependency build failed for $releaseName." }

        Write-DeployLog -Level Info -Message "Scaling release '$releaseName' to 0 replicas."
        helm upgrade --install $releaseName $chartPath `
            --namespace $NamespaceName `
            --reset-then-reuse-values `
            --set replicaCount=0 `
            --force-conflicts `
            --history-max 3 `
            --wait --timeout 10m
        if ($LASTEXITCODE -ne 0) { throw "helm upgrade to 0 replicas failed for $releaseName." }

        Assert-AksWorkloadPodCount `
            -ReleaseName $releaseName `
            -NamespaceName $NamespaceName `
            -ExpectedPodCount 0
    }

    Write-DeployPhase -Name 'Stopping AKS Workloads' -Event End
}

function Assert-AksWorkloadPodCount {
    param (
        [Parameter(Mandatory = $true)]
        [string]$ReleaseName,
        [Parameter(Mandatory = $true)]
        [string]$NamespaceName,
        [Parameter(Mandatory = $true)]
        [int]$ExpectedPodCount,
        [Parameter(Mandatory = $false)]
        [int]$TimeoutSeconds = 300
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $lastPodCount = $null
    $lastExitCode = 0
    do {
        $pods = @(kubectl get pods --namespace $NamespaceName --selector "app.kubernetes.io/instance=$ReleaseName" --output name)
        $lastExitCode = $LASTEXITCODE

        if ($lastExitCode -eq 0) {
            $lastPodCount = $pods.Count
            if ($lastPodCount -eq $ExpectedPodCount) {
                Write-DeployLog -Level Success -Message "Release '$ReleaseName' has $ExpectedPodCount pod(s) in namespace '$NamespaceName'."
                return
            }
        }
        else {
            Write-DeployLog -Level Warn -Message "kubectl get pods failed (exit code $lastExitCode) for release '$ReleaseName' in namespace '$NamespaceName'. The pod count is unknown, not zero - retrying."
        }

        Start-Sleep -Seconds 5
    } while ((Get-Date) -lt $deadline)

    if ($lastExitCode -ne 0) {
        throw "Could not verify the pod count for release '$ReleaseName' in namespace '$NamespaceName': kubectl get pods was still failing (exit code $lastExitCode) after $TimeoutSeconds seconds. The pod count is UNKNOWN, so this is NOT a confirmed scale to $ExpectedPodCount."
    }

    throw "Release '$ReleaseName' still has $lastPodCount pod(s) in namespace '$NamespaceName' after $TimeoutSeconds seconds; expected $ExpectedPodCount."
}
