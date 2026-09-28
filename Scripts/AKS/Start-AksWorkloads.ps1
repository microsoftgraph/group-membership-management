function Start-AksWorkloads {
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

    Write-DeployPhase -Name 'Starting AKS Workloads' -Event Begin

    $connection = Connect-AksCluster `
        -SolutionAbbreviation $SolutionAbbreviation `
        -EnvironmentAbbreviation $EnvironmentAbbreviation

    if (-not $connection.Available) {
        throw "Cannot start the AKS workloads: $($connection.Reason)"
    }

    foreach ($service in $Services) {
        $chartName = $service.ToLower()
        $chartPath = Join-Path $DeploymentPackagePath "AKS/charts/$chartName"
        if (-not (Test-Path $chartPath)) { throw "Chart not found: $chartPath" }

        helm status $chartName --namespace $NamespaceName --output json | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "No Helm release '$chartName' in namespace '$NamespaceName'. Set-AksWorkload must deploy the service before it can be started."
        }

        helm dependency build $chartPath
        if ($LASTEXITCODE -ne 0) { throw "helm dependency build failed for $chartName." }

        Write-DeployLog -Level Info -Message "Scaling release '$chartName' to 1 replica - this cluster now dispatches for $service."
        helm upgrade --install $chartName $chartPath `
            --namespace $NamespaceName `
            --reset-then-reuse-values `
            --set replicaCount=1 `
            --force-conflicts `
            --history-max 3 `
            --wait --timeout 10m --rollback-on-failure
        if ($LASTEXITCODE -ne 0) { throw "helm upgrade to 1 replica failed for $chartName." }

        Assert-AksWorkloadStable `
            -ReleaseName   $chartName `
            -NamespaceName $NamespaceName
    }

    Write-DeployPhase -Name 'Starting AKS Workloads' -Event End
}

function Assert-AksWorkloadStable {
    param (
        [Parameter(Mandatory = $true)]
        [string]$ReleaseName,
        [Parameter(Mandatory = $true)]
        [string]$NamespaceName,
        [Parameter(Mandatory = $false)]
        [int]$ObservationSeconds = 60
    )

    $deadline = (Get-Date).AddSeconds($ObservationSeconds)
    do {
        $podsJson = kubectl get pods --namespace $NamespaceName --selector "app.kubernetes.io/instance=$ReleaseName" --output json
        if ($LASTEXITCODE -ne 0) {
            throw "kubectl get pods failed (exit code $LASTEXITCODE) for release '$ReleaseName' in namespace '$NamespaceName'. The workload state is UNKNOWN, which is not the same as healthy."
        }

        $pods = @(($podsJson | ConvertFrom-Json).items)
        if ($pods.Count -eq 0) {
            throw "Release '$ReleaseName' has no pods in namespace '$NamespaceName', although it was just started."
        }

        foreach ($pod in $pods) {
            foreach ($container in @($pod.status.containerStatuses)) {
                if ($container.restartCount -gt 0) {
                    throw "Container '$($container.name)' in pod '$($pod.metadata.name)' has restarted $($container.restartCount) time(s) since '$ReleaseName' was started, so the host is not starting cleanly."
                }
                if ($null -ne $container.state.waiting) {
                    throw "Container '$($container.name)' in pod '$($pod.metadata.name)' is waiting: $($container.state.waiting.reason). $($container.state.waiting.message)"
                }
                if (-not $container.ready) {
                    throw "Container '$($container.name)' in pod '$($pod.metadata.name)' is no longer ready."
                }
            }
        }

        Start-Sleep -Seconds 5
    } while ((Get-Date) -lt $deadline)

    Write-DeployLog -Level Success -Message "Release '$ReleaseName' stayed ready with no restarts for $ObservationSeconds seconds."
}
