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

namespace Services.WebApi
{
    [ExcludeFromCodeCoverage]
    public class AksWorkloadService : IAksWorkloadService
    {
        private const string Namespace = "gmm";
        private const string FieldManager = "gmm-webapi";
        // Well-known application ID of the AKS AAD server, the audience of Kubernetes API tokens.
        private const string AksServerScope = "6dae42f8-4368-4678-94ff-3960e28e3630/.default";
        private static readonly TimeSpan ScaleDownTimeout = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan ReadyTimeout = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan StabilityWindow = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan RollbackTimeout = TimeSpan.FromSeconds(30);

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

            foreach (var deployment in deployments.Items)
            {
                await ScaleAsync(client, deployment.Metadata.Name, 0, cancellationToken);
            }

            foreach (var deployment in deployments.Items)
            {
                await WaitForNoPodsAsync(client, deployment, cancellationToken);
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

            var previousReplicas = deployment.Spec?.Replicas ?? 0;
            var readyReplicas = deployment.Status?.ReadyReplicas ?? 0;
            if (previousReplicas >= 1 && readyReplicas >= 1)
            {
                _logger.AksDeploymentAlreadyRunning(deploymentName, readyReplicas);
                return;
            }

            await ScaleAsync(client, deploymentName, 1, cancellationToken);
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
                    await ScaleAsync(client, deploymentName, 0, rollbackCancellation.Token);
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

        private async Task ScaleAsync(Kubernetes client, string deploymentName, int replicas, CancellationToken cancellationToken)
        {
            _logger.AksDeploymentScaling(deploymentName, Namespace, replicas);
            var patch = new V1Patch(new { spec = new { replicas } }, V1Patch.PatchType.MergePatch);
            await client.AppsV1.PatchNamespacedDeploymentAsync(patch, deploymentName, Namespace, fieldManager: FieldManager, cancellationToken: cancellationToken);
        }

        private async Task WaitForNoPodsAsync(Kubernetes client, V1Deployment deployment, CancellationToken cancellationToken)
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
                    var pods = await client.CoreV1.ListNamespacedPodAsync(Namespace, labelSelector: selector, cancellationToken: cancellationToken);
                    lastError = null;
                    lastPodCount = pods.Items.Count;
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
                var pods = await client.CoreV1.ListNamespacedPodAsync(Namespace, labelSelector: selector, cancellationToken: cancellationToken);
                if (pods.Items.Count == 0)
                {
                    throw new InvalidOperationException($"Deployment '{deploymentName}' has no pods in namespace '{Namespace}', although it was just started.");
                }

                foreach (var pod in pods.Items)
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
