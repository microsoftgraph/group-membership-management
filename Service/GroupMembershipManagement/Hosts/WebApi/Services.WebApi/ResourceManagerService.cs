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

        public async Task StartWebSitesAsync(Guid requestorId, CancellationToken cancellationToken)
        {
            _logger.GmmStarting();

            var webSites = await GetWebSitesAsync(cancellationToken);
            WebSiteResource? jobTrigger = null;

            foreach (var webSite in webSites)
            {
                // JobTrigger should be started last
                if (webSite.Data.Name.Contains("-JobTrigger", StringComparison.InvariantCultureIgnoreCase))
                {
                    jobTrigger = webSite;
                    continue;
                }

                await webSite.StartAsync(cancellationToken);
                _logger.WebsiteStarted(webSite.Data.Name);
            }

            if (jobTrigger != null)
            {
                await jobTrigger.StartAsync(cancellationToken);
                _logger.WebsiteStarted(jobTrigger.Data.Name);
            }
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
            var filteredWebsites = allWebsites.Where(x => !x.Data.Name.EndsWith("-webapi", StringComparison.InvariantCultureIgnoreCase)).ToList();
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
}
