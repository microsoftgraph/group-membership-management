// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.ContainerService;
using Azure.ResourceManager.ContainerService.Models;
using Hosts.WebApi;
using k8s;
using k8s.Authentication;
using k8s.Autorest;
using k8s.Models;
using Microsoft.Extensions.Logging;
using Services.Contracts;
using Services.Entities;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace Services.WebApi
{
    [ExcludeFromCodeCoverage]
    public class AksWorkloadService : IAksWorkloadService
    {
        private const string Namespace = "gmm";
        private const string FieldManager = "gmm-webapi";
        private const string KedaGroup = "keda.sh";
        private const string KedaVersion = "v1alpha1";
        private const string ScaledObjectPlural = "scaledobjects";
        // Well-known application ID of the AKS AAD server, the audience of Kubernetes API tokens.
        private const string AksServerScope = "6dae42f8-4368-4678-94ff-3960e28e3630/.default";
        private static readonly TimeSpan ScaleDownTimeout = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan ReadyTimeout = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan StabilityWindow = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan RollbackTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan AutoscaledReadyTimeout = TimeSpan.FromMinutes(15);
        // KEDA reconciles a ScaledObject about every 30 seconds; zero must hold across several passes.
        private static readonly TimeSpan PausedObservationWindow = TimeSpan.FromSeconds(90);
        private static readonly TimeSpan AutoscaledStopMargin = TimeSpan.FromSeconds(240);
        private static readonly TimeSpan DefaultTerminationGrace = TimeSpan.FromSeconds(60);
        private const long MaximumTerminationGraceSeconds = 1560;

        private readonly DefaultAzureCredential _credential;
        private readonly ContainerServiceManagedClusterResource _cluster;
        private readonly string _clusterName;
        private readonly ILogger<AksWorkloadService> _logger;

        public AksWorkloadService(ResourceManagerServiceConfiguration serviceConfiguration,
                                  ILogger<AksWorkloadService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _credential = new DefaultAzureCredential(DefaultAzureCredential.DefaultEnvironmentVariableName);
            _clusterName = $"{serviceConfiguration.ComputeResourceGroup}-aks";

            var client = new ArmClient(_credential, serviceConfiguration.SubscriptionId);
            var clusterId = ContainerServiceManagedClusterResource.CreateResourceIdentifier(
                                serviceConfiguration.SubscriptionId,
                                serviceConfiguration.ComputeResourceGroup,
                                _clusterName);
            _cluster = client.GetContainerServiceManagedClusterResource(clusterId);
        }

        public async Task StopAllWorkloadsAsync(CancellationToken cancellationToken)
        {
            using var client = await ConnectAsync(requireAvailable: false, cancellationToken);
            if (client == null)
            {
                return;
            }

            var deployments = await client.AppsV1.ListNamespacedDeploymentAsync(Namespace, cancellationToken: cancellationToken);
            if (deployments.Items.Count == 0)
            {
                _logger.AksNoDeployments(Namespace);
                return;
            }

            // KEDA's HPA owns the replica count of an autoscaled Deployment and would immediately undo a
            // write to spec.replicas, so those are stopped by pausing their ScaledObject at zero instead.
            var scaledObjects = await GetScaledObjectsByDeploymentAsync(client, cancellationToken);

            foreach (var deployment in deployments.Items)
            {
                if (scaledObjects.TryGetValue(deployment.Metadata.Name, out var scaledObject))
                {
                    await PauseScaledObjectAtZeroAsync(client, deployment.Metadata.Name, scaledObject.Name, cancellationToken);
                }
                else
                {
                    await SetDeploymentReplicasAsync(client, deployment.Metadata.Name, 0, cancellationToken);
                }
            }

            foreach (var deployment in deployments.Items)
            {
                if (scaledObjects.TryGetValue(deployment.Metadata.Name, out var scaledObject))
                {
                    await WaitForAutoscaledWorkloadStoppedAsync(client, deployment, scaledObject.Name, cancellationToken);
                }
                else
                {
                    await WaitForDeploymentScaledToZeroAsync(client, deployment, cancellationToken);
                }
            }
        }

        public async Task StartWorkloadAsync(string deploymentName, CancellationToken cancellationToken)
        {
            using var client = (await ConnectAsync(requireAvailable: true, cancellationToken))!;

            V1Deployment deployment;
            try
            {
                deployment = await client.AppsV1.ReadNamespacedDeploymentAsync(deploymentName, Namespace, cancellationToken: cancellationToken);
            }
            catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new InvalidOperationException($"No Deployment '{deploymentName}' in namespace '{Namespace}'; the deployment must install it before it can be started.", ex);
            }

            var scaledObjects = await GetScaledObjectsByDeploymentAsync(client, cancellationToken);
            if (scaledObjects.TryGetValue(deploymentName, out var scaledObject))
            {
                await StartAutoscaledWorkloadAsync(client, deployment, scaledObject, cancellationToken);
                return;
            }

            var previousReplicas = deployment.Spec?.Replicas ?? 0;
            var readyReplicas = deployment.Status?.ReadyReplicas ?? 0;
            if (previousReplicas >= 1 && readyReplicas >= 1)
            {
                _logger.AksDeploymentAlreadyRunning(deploymentName, readyReplicas);
                return;
            }

            await SetDeploymentReplicasAsync(client, deploymentName, 1, cancellationToken);
            try
            {
                await WaitForReadyAsync(client, deploymentName, cancellationToken);
                await AssertStableAsync(client, deployment, cancellationToken);
            }
            catch when (previousReplicas == 0)
            {
                try
                {
                    using var rollbackCancellation = new CancellationTokenSource(RollbackTimeout);
                    await SetDeploymentReplicasAsync(client, deploymentName, 0, rollbackCancellation.Token);
                }
                catch (Exception rollbackException)
                {
                    _logger.AksStartRollbackFailed(deploymentName, rollbackException);
                }

                throw;
            }

            _logger.AksDeploymentReady(deploymentName, (int)StabilityWindow.TotalSeconds);
        }

        private async Task<Kubernetes?> ConnectAsync(bool requireAvailable, CancellationToken cancellationToken)
        {
            ContainerServiceManagedClusterResource cluster;
            try
            {
                cluster = (await _cluster.GetAsync(cancellationToken)).Value;
            }
            catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
            {
                _logger.AksClusterNotFound(_clusterName);
                if (requireAvailable)
                {
                    throw new InvalidOperationException($"AKS cluster '{_clusterName}' was not found.", ex);
                }

                return null;
            }

            if (cluster.Data.PowerStateCode == ContainerServiceStateCode.Stopped)
            {
                _logger.AksClusterStopped(_clusterName);
                if (requireAvailable)
                {
                    throw new InvalidOperationException($"AKS cluster '{_clusterName}' is stopped.");
                }

                return null;
            }

            // Only the API server address and CA are read from the kubeconfig; authentication uses the WebApi's own Entra token.
            var credentials = (await cluster.GetClusterUserCredentialsAsync(cancellationToken: cancellationToken)).Value;
            using var kubeconfigStream = new MemoryStream(credentials.Kubeconfigs[0].Value);
            var kubeconfig = await KubernetesClientConfiguration.LoadKubeConfigAsync(kubeconfigStream);
            var endpoint = kubeconfig.Clusters.First().ClusterEndpoint;

            var caCerts = new X509Certificate2Collection();
            caCerts.ImportFromPem(Encoding.UTF8.GetString(Convert.FromBase64String(endpoint.CertificateAuthorityData)));

            var configuration = new KubernetesClientConfiguration
            {
                Host = endpoint.Server,
                SslCaCerts = caCerts,
                TokenProvider = new EntraTokenProvider(_credential)
            };

            return new Kubernetes(configuration);
        }

        private async Task<IReadOnlyDictionary<string, KedaScaledObject>> GetScaledObjectsByDeploymentAsync(Kubernetes client, CancellationToken cancellationToken)
        {
            JsonElement list;
            try
            {
                list = await client.CustomObjects.ListNamespacedCustomObjectAsync<JsonElement>(KedaGroup, KedaVersion, Namespace, ScaledObjectPlural, cancellationToken: cancellationToken);
            }
            catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
            {
                // The KEDA CRD is not installed, so no Deployment in the cluster is autoscaled.
                return new Dictionary<string, KedaScaledObject>(StringComparer.Ordinal);
            }
            catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new InvalidOperationException($"The WebApi cannot read KEDA ScaledObjects in namespace '{Namespace}', so which workloads are autoscaled is UNKNOWN. Grant it the AKS custom resource role (Set-WebAPIAccessRoles with DeployAks).", ex);
            }

            var byTarget = new Dictionary<string, KedaScaledObject>(StringComparer.Ordinal);
            foreach (var scaledObject in KedaScaledObject.ParseList(list))
            {
                if (!byTarget.TryAdd(scaledObject.TargetName, scaledObject))
                {
                    throw new InvalidOperationException($"More than one ScaledObject targets Deployment '{scaledObject.TargetName}' in namespace '{Namespace}'; its replica owner is ambiguous.");
                }
            }

            return byTarget;
        }

        private async Task PauseScaledObjectAtZeroAsync(Kubernetes client, string deploymentName, string scaledObjectName, CancellationToken cancellationToken)
        {
            _logger.AksScaledObjectPausing(deploymentName, scaledObjectName);
            await client.CustomObjects.PatchNamespacedCustomObjectAsync(KedaScaledObject.PauseAtZeroPatch(), KedaGroup, KedaVersion, Namespace, ScaledObjectPlural, scaledObjectName, fieldManager: FieldManager, cancellationToken: cancellationToken);
        }

        private async Task ResumeScaledObjectAsync(Kubernetes client, string deploymentName, string scaledObjectName, CancellationToken cancellationToken)
        {
            _logger.AksScaledObjectResuming(deploymentName, scaledObjectName);
            await client.CustomObjects.PatchNamespacedCustomObjectAsync(KedaScaledObject.ResumePatch(), KedaGroup, KedaVersion, Namespace, ScaledObjectPlural, scaledObjectName, fieldManager: FieldManager, cancellationToken: cancellationToken);
        }

        private async Task<KedaScaledObject> ReadScaledObjectAsync(Kubernetes client, string scaledObjectName, CancellationToken cancellationToken)
        {
            var item = await client.CustomObjects.GetNamespacedCustomObjectAsync<JsonElement>(KedaGroup, KedaVersion, Namespace, ScaledObjectPlural, scaledObjectName, cancellationToken: cancellationToken);
            return KedaScaledObject.Parse(item);
        }

        private static async Task<IReadOnlyList<HpaState>> GetTargetingHpasAsync(Kubernetes client, string deploymentName, CancellationToken cancellationToken)
        {
            var hpas = await client.AutoscalingV2.ListNamespacedHorizontalPodAutoscalerAsync(Namespace, cancellationToken: cancellationToken);
            return hpas.Items
                .Where(hpa => hpa.Spec?.ScaleTargetRef?.Name == deploymentName &&
                              hpa.Spec.ScaleTargetRef.Kind == "Deployment")
                .Select(hpa => new HpaState(
                    hpa.Metadata.OwnerReferences?.FirstOrDefault(owner => owner.Kind == "ScaledObject")?.Uid,
                    hpa.Status?.Conditions?.Any(condition => condition.Type == "ScalingActive" && condition.Status == "True") == true))
                .ToList();
        }

        private async Task WaitForAutoscaledWorkloadStoppedAsync(Kubernetes client, V1Deployment deployment, string scaledObjectName, CancellationToken cancellationToken)
        {
            var deploymentName = deployment.Metadata.Name;
            var selector = GetLabelSelector(deployment);
            var stopTimeout = GetAutoscaledStopTimeout(deployment);
            var deadline = DateTime.UtcNow + stopTimeout;
            DateTime? zeroSince = null;
            Exception? lastError = null;

            do
            {
                try
                {
                    var scaledObject = await ReadScaledObjectAsync(client, scaledObjectName, cancellationToken);
                    var hpas = await GetTargetingHpasAsync(client, deploymentName, cancellationToken);
                    var current = await client.AppsV1.ReadNamespacedDeploymentAsync(deploymentName, Namespace, cancellationToken: cancellationToken);
                    var pods = LivePods(await client.CoreV1.ListNamespacedPodAsync(Namespace, labelSelector: selector, cancellationToken: cancellationToken));
                    lastError = null;

                    if (KedaWorkloadState.IsPausedAtZero(scaledObject, hpas, current.Spec?.Replicas, pods.Count))
                    {
                        zeroSince ??= DateTime.UtcNow;
                        if (DateTime.UtcNow - zeroSince >= PausedObservationWindow)
                        {
                            _logger.AksScaledObjectStopped(deploymentName, (int)PausedObservationWindow.TotalSeconds);
                            return;
                        }
                    }
                    else
                    {
                        zeroSince = null;
                    }
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    zeroSince = null;
                    lastError = ex;
                    _logger.AksScalingStateUnknown(deploymentName, ex);
                }

                await Task.Delay(PollInterval, cancellationToken);
            } while (DateTime.UtcNow < deadline);

            if (lastError != null)
            {
                throw new InvalidOperationException($"Could not verify that autoscaled Deployment '{deploymentName}' in namespace '{Namespace}' is paused at zero after {stopTimeout.TotalSeconds} seconds. Its state is UNKNOWN, so this is NOT a confirmed stop.", lastError);
            }

            throw new InvalidOperationException($"Autoscaled Deployment '{deploymentName}' in namespace '{Namespace}' did not stay paused at zero, with no scaling HPA and no pods, for {PausedObservationWindow.TotalSeconds} seconds within its {stopTimeout.TotalSeconds}-second stop budget.");
        }

        internal static TimeSpan GetAutoscaledStopTimeout(V1Deployment deployment)
        {
            var graceSeconds = deployment.Spec?.Template?.Spec?.TerminationGracePeriodSeconds;
            var grace = graceSeconds is > 0 and <= MaximumTerminationGraceSeconds
                ? TimeSpan.FromSeconds(graceSeconds.Value)
                : DefaultTerminationGrace;
            return grace + AutoscaledStopMargin;
        }

        private async Task StartAutoscaledWorkloadAsync(Kubernetes client, V1Deployment deployment, KedaScaledObject scaledObject, CancellationToken cancellationToken)
        {
            var deploymentName = deployment.Metadata.Name;
            if (!scaledObject.HasPauseRequest)
            {
                var hpas = await GetTargetingHpasAsync(client, deploymentName, cancellationToken);
                if (KedaWorkloadState.IsActive(scaledObject, hpas, deployment.Spec?.Replicas, deployment.Status?.ReadyReplicas, deployment.Status?.ObservedGeneration, deployment.Metadata.Generation))
                {
                    _logger.AksDeploymentAlreadyRunning(deploymentName, deployment.Status?.ReadyReplicas ?? 0);
                    return;
                }
            }

            await ResumeScaledObjectAsync(client, deploymentName, scaledObject.Name, cancellationToken);
            try
            {
                var readyReplicas = await WaitForAutoscaledWorkloadActiveAsync(client, deploymentName, scaledObject.Name, cancellationToken);
                await AssertAutoscaledStableAsync(client, deployment, scaledObject.Name, cancellationToken);
                _logger.AksAutoscaledDeploymentReady(deploymentName, readyReplicas, (int)StabilityWindow.TotalSeconds);
            }
            catch
            {
                try
                {
                    using var rollbackCancellation = new CancellationTokenSource(RollbackTimeout);
                    await PauseScaledObjectAtZeroAsync(client, deploymentName, scaledObject.Name, rollbackCancellation.Token);
                }
                catch (Exception rollbackException)
                {
                    _logger.AksStartRollbackFailed(deploymentName, rollbackException);
                }

                throw;
            }
        }

        private async Task<int> WaitForAutoscaledWorkloadActiveAsync(Kubernetes client, string deploymentName, string scaledObjectName, CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow + AutoscaledReadyTimeout;
            while (true)
            {
                var scaledObject = await ReadScaledObjectAsync(client, scaledObjectName, cancellationToken);
                var hpas = await GetTargetingHpasAsync(client, deploymentName, cancellationToken);
                var deployment = await client.AppsV1.ReadNamespacedDeploymentAsync(deploymentName, Namespace, cancellationToken: cancellationToken);
                if (KedaWorkloadState.IsActive(scaledObject, hpas, deployment.Spec?.Replicas, deployment.Status?.ReadyReplicas, deployment.Status?.ObservedGeneration, deployment.Metadata.Generation))
                {
                    return deployment.Status!.ReadyReplicas!.Value;
                }

                if (DateTime.UtcNow >= deadline)
                {
                    throw new InvalidOperationException($"Autoscaled Deployment '{deploymentName}' in namespace '{Namespace}' was not active after {AutoscaledReadyTimeout.TotalMinutes} minutes: KEDA must acknowledge the resume, own a scaling HPA, and every desired replica must be ready.");
                }

                await Task.Delay(PollInterval, cancellationToken);
            }
        }

        // The HPA may add replicas during the window, so a pod that is still starting is not a failure;
        // a restart, a crash loop or the autoscaler disappearing is.
        private async Task AssertAutoscaledStableAsync(Kubernetes client, V1Deployment deployment, string scaledObjectName, CancellationToken cancellationToken)
        {
            var deploymentName = deployment.Metadata.Name;
            var selector = GetLabelSelector(deployment);
            var deadline = DateTime.UtcNow + StabilityWindow;

            do
            {
                var scaledObject = await ReadScaledObjectAsync(client, scaledObjectName, cancellationToken);
                var hpas = await GetTargetingHpasAsync(client, deploymentName, cancellationToken);
                if (scaledObject.HasPauseRequest || scaledObject.Condition("Paused") == "True" || hpas.Count != 1 || hpas[0].OwnerScaledObjectUid != scaledObject.Uid)
                {
                    throw new InvalidOperationException($"Autoscaled Deployment '{deploymentName}' in namespace '{Namespace}' lost its active autoscaler while it was starting.");
                }

                var pods = LivePods(await client.CoreV1.ListNamespacedPodAsync(Namespace, labelSelector: selector, cancellationToken: cancellationToken));
                if (pods.Count == 0)
                {
                    throw new InvalidOperationException($"Deployment '{deploymentName}' has no pods in namespace '{Namespace}', although it was just started.");
                }

                foreach (var pod in pods)
                {
                    foreach (var container in pod.Status?.ContainerStatuses ?? [])
                    {
                        if (container.RestartCount > 0)
                        {
                            throw new InvalidOperationException($"Container '{container.Name}' in pod '{pod.Metadata.Name}' has restarted {container.RestartCount} time(s) since '{deploymentName}' was started, so the host is not starting cleanly.");
                        }

                        var waitingReason = container.State?.Waiting?.Reason;
                        if (waitingReason != null && waitingReason != "ContainerCreating" && waitingReason != "PodInitializing")
                        {
                            throw new InvalidOperationException($"Container '{container.Name}' in pod '{pod.Metadata.Name}' is waiting: {waitingReason}. {container.State!.Waiting!.Message}");
                        }
                    }
                }

                await Task.Delay(PollInterval, cancellationToken);
            } while (DateTime.UtcNow < deadline);
        }

        private async Task SetDeploymentReplicasAsync(Kubernetes client, string deploymentName, int replicas, CancellationToken cancellationToken)
        {
            _logger.AksDeploymentScaling(deploymentName, Namespace, replicas);
            var patch = new V1Patch(new { spec = new { replicas } }, V1Patch.PatchType.MergePatch);
            await client.AppsV1.PatchNamespacedDeploymentAsync(patch, deploymentName, Namespace, fieldManager: FieldManager, cancellationToken: cancellationToken);
        }

        private async Task WaitForDeploymentScaledToZeroAsync(Kubernetes client, V1Deployment deployment, CancellationToken cancellationToken)
        {
            var deploymentName = deployment.Metadata.Name;
            var selector = GetLabelSelector(deployment);
            var deadline = DateTime.UtcNow + ScaleDownTimeout;
            int? lastPodCount = null;
            Exception? lastError = null;

            do
            {
                try
                {
                    var pods = LivePods(await client.CoreV1.ListNamespacedPodAsync(Namespace, labelSelector: selector, cancellationToken: cancellationToken));
                    lastError = null;
                    lastPodCount = pods.Count;
                    if (lastPodCount == 0)
                    {
                        _logger.AksDeploymentScaledToZero(deploymentName);
                        return;
                    }
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    lastError = ex;
                    _logger.AksPodCountUnknown(deploymentName, ex);
                }

                await Task.Delay(PollInterval, cancellationToken);
            } while (DateTime.UtcNow < deadline);

            if (lastError != null)
            {
                throw new InvalidOperationException($"Could not verify the pod count for Deployment '{deploymentName}' in namespace '{Namespace}' after {ScaleDownTimeout.TotalSeconds} seconds. The pod count is UNKNOWN, so this is NOT a confirmed scale to 0.", lastError);
            }

            throw new InvalidOperationException($"Deployment '{deploymentName}' still has {lastPodCount} pod(s) in namespace '{Namespace}' after {ScaleDownTimeout.TotalSeconds} seconds; expected 0.");
        }

        private async Task WaitForReadyAsync(Kubernetes client, string deploymentName, CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow + ReadyTimeout;
            while (true)
            {
                var deployment = await client.AppsV1.ReadNamespacedDeploymentAsync(deploymentName, Namespace, cancellationToken: cancellationToken);
                if (deployment.Status?.ObservedGeneration >= deployment.Metadata.Generation && deployment.Status?.ReadyReplicas == 1)
                {
                    return;
                }

                if (DateTime.UtcNow >= deadline)
                {
                    throw new InvalidOperationException($"Deployment '{deploymentName}' in namespace '{Namespace}' was not ready after {ReadyTimeout.TotalMinutes} minutes.");
                }

                await Task.Delay(PollInterval, cancellationToken);
            }
        }

        private async Task AssertStableAsync(Kubernetes client, V1Deployment deployment, CancellationToken cancellationToken)
        {
            var deploymentName = deployment.Metadata.Name;
            var selector = GetLabelSelector(deployment);
            var deadline = DateTime.UtcNow + StabilityWindow;

            do
            {
                var pods = LivePods(await client.CoreV1.ListNamespacedPodAsync(Namespace, labelSelector: selector, cancellationToken: cancellationToken));
                if (pods.Count == 0)
                {
                    throw new InvalidOperationException($"Deployment '{deploymentName}' has no pods in namespace '{Namespace}', although it was just started.");
                }

                foreach (var pod in pods)
                {
                    foreach (var container in pod.Status?.ContainerStatuses ?? [])
                    {
                        if (container.RestartCount > 0)
                        {
                            throw new InvalidOperationException($"Container '{container.Name}' in pod '{pod.Metadata.Name}' has restarted {container.RestartCount} time(s) since '{deploymentName}' was started, so the host is not starting cleanly.");
                        }

                        if (container.State?.Waiting != null)
                        {
                            throw new InvalidOperationException($"Container '{container.Name}' in pod '{pod.Metadata.Name}' is waiting: {container.State.Waiting.Reason}. {container.State.Waiting.Message}");
                        }

                        if (!container.Ready)
                        {
                            throw new InvalidOperationException($"Container '{container.Name}' in pod '{pod.Metadata.Name}' is no longer ready.");
                        }
                    }
                }

                await Task.Delay(PollInterval, cancellationToken);
            } while (DateTime.UtcNow < deadline);
        }

        // A pod the kubelet killed (for example "Preempted in order to admit critical pod") stays listed as Failed
        // until pod garbage collection, so a Deployment's selector keeps matching a pod that no longer counts.
        internal static IReadOnlyList<V1Pod> LivePods(V1PodList pods) =>
            pods.Items.Where(pod => pod.Status?.Phase is not ("Failed" or "Succeeded")).ToList();

        private static string GetLabelSelector(V1Deployment deployment)
        {
            var matchLabels = deployment.Spec?.Selector?.MatchLabels;
            if (matchLabels == null || matchLabels.Count == 0)
            {
                throw new InvalidOperationException($"Deployment '{deployment.Metadata.Name}' in namespace '{Namespace}' has no selector matchLabels, so its pods cannot be found.");
            }

            return string.Join(",", matchLabels.Select(label => $"{label.Key}={label.Value}"));
        }

        private sealed class EntraTokenProvider : ITokenProvider
        {
            private readonly TokenCredential _credential;

            public EntraTokenProvider(TokenCredential credential)
            {
                _credential = credential;
            }

            public async Task<AuthenticationHeaderValue> GetAuthenticationHeaderAsync(CancellationToken cancellationToken)
            {
                var token = await _credential.GetTokenAsync(new TokenRequestContext([AksServerScope]), cancellationToken);
                return new AuthenticationHeaderValue("Bearer", token.Token);
            }
        }
    }
}
