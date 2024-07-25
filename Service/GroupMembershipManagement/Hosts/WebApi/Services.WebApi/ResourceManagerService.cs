// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.AppService;
using Azure.ResourceManager.Resources;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Entities;
using System.Text.RegularExpressions;

namespace Services.WebApi
{
    public class ResourceManagerService : IResourceManagerService
    {
        private readonly ArmClient _client;
        private readonly SubscriptionResource _subscription;
        private readonly string _dataResourceGroupName;
        private readonly string _computeResourceGroupName;
        private readonly ILoggingRepository _loggingRepository;

        private ResourceGroupResource _computeResourceGroup;
        private ResourceGroupResource _dataResourceGroup;

        public ResourceManagerService(ResourceManagerServiceConfiguration serviceConfiguration,
                                      ILoggingRepository loggingRepository)
        {
            var subscritpionResourceId = SubscriptionResource.CreateResourceIdentifier(serviceConfiguration.SubscriptionId);
            _client = new ArmClient(new DefaultAzureCredential(), serviceConfiguration.SubscriptionId);
            _subscription = _client.GetSubscriptionResource(subscritpionResourceId);
            _dataResourceGroupName = serviceConfiguration.DataResourceGroup;
            _computeResourceGroupName = serviceConfiguration.ComputeResourceGroup;
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        public async Task ResetWebSitesAsync(Guid requestorId, CancellationToken cancellationToken)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = "Resetting GMM..."
            });

            var webSites = await GetWebSitesAsync(cancellationToken);
            foreach (var webSite in webSites)
            {
                Console.WriteLine($"Stopping {webSite.Data.Name}");
                await webSite.StopAsync();
            }

            var keyvaultNamePattern = @"SecretUri=https://(?<kvName>.*)?\.vault.azure.net";
            var secretNamePattern = @"(.*\/)(?<secretName>.*)\/(?<version>.*)";

            foreach (var webSite in webSites)
            {
                var appSettings = await webSite.GetApplicationSettingsAsync(cancellationToken);
                foreach (var appSetting in appSettings.Value.Properties)
                {
                    if (appSetting.Key.Equals("WEBSITE_CONTENTAZUREFILECONNECTIONSTRING", StringComparison.InvariantCultureIgnoreCase))
                    {
                        var keyvaultNameMatch = Regex.Match(appSetting.Value, keyvaultNamePattern);
                        var secretNameMatch = Regex.Match(appSetting.Value, secretNamePattern);
                        Console.WriteLine(secretNameMatch.Groups["secretName"]);
                    }
                }
            }

            foreach (var webSite in webSites)
            {
                await webSite.StartAsync(cancellationToken);
            }
        }

        public async Task StopWebSitesAsync(Guid requestorId, CancellationToken cancellationToken)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = "Stopping GMM..."
            });

            var webSites = await GetWebSitesAsync(cancellationToken);
            foreach (var webSite in webSites)
            {
                await webSite.StopAsync(cancellationToken);
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Stopped {webSite.Data.Name}"
                });
            }
        }

        public async Task StartWebSitesAsync(Guid requestorId, CancellationToken cancellationToken)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = "Starting GMM..."
            });

            var webSites = await GetWebSitesAsync(cancellationToken);
            foreach (var webSite in webSites)
            {
                await webSite.StartAsync(cancellationToken);
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Started {webSite.Data.Name}"
                });
            }
        }

        private async Task<List<WebSiteResource>> GetWebSitesAsync(CancellationToken cancellationToken)
        {
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
