// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.AppService;
using Azure.ResourceManager.Resources;
using Azure.Security.KeyVault.Secrets;
using Hosts.WebApi;
using Microsoft.Extensions.Logging;
using Services.Contracts;
using Services.Entities;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

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

        public async Task<Dictionary<string, string>> GetWebSitesStorageAccountsAsync(CancellationToken cancellationToken)
        {
            _logger.GettingStorageAccountNames();

            var secretNamePattern = @"(.*\/)(?<secretName>.*)\/(?<version>.*)";
            var keyvaultNamePattern = @"SecretUri=https://(?<kvName>.*)?\.vault.azure.net";
            var webSites = await GetWebSitesAsync(cancellationToken);

            // keyvault name, secret information
            var kvSecrets = new Dictionary<string, List<SecretInformation>>();

            foreach (var webSite in webSites)
            {
                var appSettings = await webSite.GetApplicationSettingsAsync(cancellationToken);
                foreach (var appSetting in appSettings.Value.Properties)
                {
                    if (appSetting.Key.Equals("WEBSITE_CONTENTAZUREFILECONNECTIONSTRING", StringComparison.InvariantCultureIgnoreCase))
                    {
                        var secretNameMatch = Regex.Match(appSetting.Value, secretNamePattern);
                        var keyvaultNameMatch = Regex.Match(appSetting.Value, keyvaultNamePattern);

                        if (secretNameMatch.Groups["secretName"].Success && keyvaultNameMatch.Groups["kvName"].Success)
                        {
                            var keyvaultName = keyvaultNameMatch.Groups["kvName"].Value;
                            var secretName = secretNameMatch.Groups["secretName"].Value;

                            if (kvSecrets.ContainsKey(keyvaultName))
                            {
                                kvSecrets[keyvaultName].Add(new SecretInformation
                                {
                                    SecretName = secretName,
                                    FunctionName = webSite.Data.Name
                                });
                            }
                            else
                            {
                                kvSecrets.Add(keyvaultName, new List<SecretInformation>
                                {
                                    new SecretInformation
                                    {
                                        SecretName = secretName,
                                        FunctionName = webSite.Data.Name
                                    }
                                });
                            }
                        }
                    }
                }
            }

            var accountNamePattern = @"AccountName=(?<accountName>.*?);";

            foreach (var kvSecret in kvSecrets)
            {
                var kvUri = "https://" + kvSecret.Key + ".vault.azure.net";

                DefaultAzureCredential credential = new(DefaultAzureCredential.DefaultEnvironmentVariableName);
                var client = new SecretClient(new Uri(kvUri), credential);

                foreach (var secretName in kvSecret.Value)
                {
                    var secret = await client.GetSecretAsync(secretName.SecretName, cancellationToken: cancellationToken);
                    var accountNameMatch = Regex.Match(secret.Value.Value, accountNamePattern);
                    if (accountNameMatch.Groups["accountName"].Success)
                    {
                        secretName.SecretValue = accountNameMatch.Groups["accountName"].Value;
                    }
                }
            }

            // FunctionName, StorageAccountName
            return kvSecrets.Values.SelectMany(x => x).ToList().ToDictionary(x => x.FunctionName, x => x.SecretValue);
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

        private class SecretInformation
        {
            public string SecretName { get; set; }
            public string SecretValue { get; set; }
            public string FunctionName { get; set; }
        }

    }
}
