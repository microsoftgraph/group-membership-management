// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.AppService;
using Azure.ResourceManager.Resources;
using Hosts.WebApi;
using Microsoft.Extensions.Logging;
using Services.Contracts;
using Services.Entities;
using System.Diagnostics.CodeAnalysis;

namespace Services.WebApi
{
    [ExcludeFromCodeCoverage]
    public class ResourceManagerService : IResourceManagerService
    {
        private const string JobTriggerService = "JobTrigger";

        private readonly ArmClient _client;
        private readonly SubscriptionResource _subscription;
        private readonly string _dataResourceGroupName;
        private readonly string _computeResourceGroupName;
        private readonly ILogger<ResourceManagerService> _logger;

        private ResourceGroupResource _computeResourceGroup;
        private ResourceGroupResource _dataResourceGroup;

        public ResourceManagerService(ResourceManagerServiceConfiguration serviceConfiguration,
                                      ILogger<ResourceManagerService> logger)
        {
            DefaultAzureCredential credential = new(DefaultAzureCredential.DefaultEnvironmentVariableName);

            var subscriptionResourceId = SubscriptionResource.CreateResourceIdentifier(serviceConfiguration.SubscriptionId);
            _client = new ArmClient(credential, serviceConfiguration.SubscriptionId);
            _subscription = _client.GetSubscriptionResource(subscriptionResourceId);
            _dataResourceGroupName = serviceConfiguration.DataResourceGroup;
            _computeResourceGroupName = serviceConfiguration.ComputeResourceGroup;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task StopWebSitesAsync(Guid requestorId, CancellationToken cancellationToken)
        {
            _logger.GmmStopping();

            var webSites = await GetWebSitesAsync(cancellationToken);
            foreach (var webSite in webSites)
            {
                await webSite.StopAsync(cancellationToken);
                _logger.WebsiteStopped(webSite.Data.Name);
            }
        }

        public async Task StartWebSitesAsync(Guid requestorId, IReadOnlyCollection<string> servicesOnAks, CancellationToken cancellationToken)
        {
            _logger.GmmStarting();

            var webSites = (await GetWebSitesAsync(cancellationToken)).ToDictionary(x => x.Data.Name, StringComparer.OrdinalIgnoreCase);
            var classification = ClassifyWebSites(webSites.Keys, _computeResourceGroupName, servicesOnAks);

            foreach (var service in classification.UnmatchedServices)
            {
                _logger.AksServiceWebsiteNotFound(service, $"{_computeResourceGroupName}-{service}");
            }

            foreach (var siteName in classification.KeptStopped)
            {
                var webSite = webSites[siteName];
                if (webSite.Data.State != "Stopped")
                {
                    await webSite.StopAsync(cancellationToken);
                }

                _logger.WebsiteKeptStoppedOnAks(siteName);
            }

            foreach (var siteName in classification.ToStart)
            {
                await webSites[siteName].StartAsync(cancellationToken);
                _logger.WebsiteStarted(siteName);
            }
        }

        public async Task StartJobTriggerWebSiteAsync(Guid requestorId, CancellationToken cancellationToken)
        {
            var websiteName = $"{_computeResourceGroupName}-{JobTriggerService}";

            await GetResourceGroupsAsync(cancellationToken);
            var website = await _computeResourceGroup.GetWebSiteAsync(websiteName, cancellationToken);
            await website.Value.StartAsync(cancellationToken);
            _logger.WebsiteStarted(websiteName);
        }

        private static WebSiteClassification ClassifyWebSites(IEnumerable<string> siteNames, string computeResourceGroupName, IReadOnlyCollection<string> servicesOnAks)
        {
            var jobTriggerSiteName = $"{computeResourceGroupName}-{JobTriggerService}";
            var toStart = new List<string>();
            var keptStopped = new List<string>();
            var matchedServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var siteName in siteNames)
            {
                var service = servicesOnAks.FirstOrDefault(s => siteName.Equals($"{computeResourceGroupName}-{s}", StringComparison.OrdinalIgnoreCase));
                if (service != null)
                {
                    matchedServices.Add(service);
                    keptStopped.Add(siteName);
                }
                else if (!siteName.Equals(jobTriggerSiteName, StringComparison.OrdinalIgnoreCase))
                {
                    toStart.Add(siteName);
                }
            }

            var unmatchedServices = servicesOnAks.Where(s => !matchedServices.Contains(s)).ToList();
            return new WebSiteClassification(toStart, keptStopped, unmatchedServices);
        }

        public async Task StartWebSiteAsync(Guid requestorId, string websiteName, CancellationToken cancellationToken)
        {
            _logger.WebsiteStartingNamed(websiteName);

            try
            {
                await GetResourceGroupsAsync(cancellationToken);
                var websiteResponse = _computeResourceGroup.GetWebSite(websiteName, cancellationToken);
                var website = websiteResponse.Value;

                var response = await website.StartAsync(cancellationToken);
                _logger.WebsiteStartResponseStatus(websiteName, response.Status);

                var retryCount = 3;
                var currentAttempts = 1;

                do
                {
                    await Task.Delay(currentAttempts++ * 5000, cancellationToken);
                    website = _computeResourceGroup.GetWebSite(websiteName, cancellationToken);
                    if (website.Data.State == "Running")
                    {
                        break;
                    }

                } while (currentAttempts <= retryCount);

                _logger.WebsiteStateAfterStart(websiteName, website.Data.State);

                // Give the website time to fully start
                await Task.Delay(10000, cancellationToken);
            }
            catch (Azure.RequestFailedException rex) when (rex.Status == 404)
            {
                _logger.WebsiteNotFound(websiteName);
            }
            catch (Exception ex)
            {
                _logger.WebsiteStartFailed(websiteName, ex);
            }

        }

        private async Task<List<WebSiteResource>> GetWebSitesAsync(CancellationToken cancellationToken)
        {
            _logger.GettingWebsites();

            await GetResourceGroupsAsync(cancellationToken);
            var allWebsites = _computeResourceGroup.GetWebSites();
            var filteredWebsites = allWebsites.AsEnumerable().Where(x => !x.Data.Name.EndsWith("-webapi", StringComparison.InvariantCultureIgnoreCase)).ToList();
            return filteredWebsites;
        }

        private async Task GetResourceGroupsAsync(CancellationToken cancellationToken)
        {
            if (_computeResourceGroup == null && _dataResourceGroup == null)
            {
                var resourceGroupTasks = new List<Task<Azure.Response<ResourceGroupResource>>>
                {
                    _subscription.GetResourceGroupAsync(_computeResourceGroupName, cancellationToken),
                    _subscription.GetResourceGroupAsync(_dataResourceGroupName, cancellationToken)
                };

                var resourceGroups = await Task.WhenAll(resourceGroupTasks);
                _computeResourceGroup = resourceGroups
                                        .Where(r => r.Value.Data.Name.Equals(_computeResourceGroupName, StringComparison.InvariantCultureIgnoreCase))
                                        .First().Value;

                _dataResourceGroup = resourceGroups
                                     .Where(r => r.Value.Data.Name.Equals(_dataResourceGroupName, StringComparison.InvariantCultureIgnoreCase))
                                     .First().Value;
            }
        }

    }

    internal sealed record WebSiteClassification(IReadOnlyList<string> ToStart, IReadOnlyList<string> KeptStopped, IReadOnlyList<string> UnmatchedServices);
}
