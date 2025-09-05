// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.AppService;
using Azure.ResourceManager.Resources;
using Azure.Security.KeyVault.Secrets;
using Models;
using Repositories.Contracts;
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
        private readonly ILoggingRepository _loggingRepository;

        private ResourceGroupResource _computeResourceGroup;
        private ResourceGroupResource _dataResourceGroup;

        public ResourceManagerService(ResourceManagerServiceConfiguration serviceConfiguration,
                                      ILoggingRepository loggingRepository)
        {
            TokenCredential credential;
#if DEBUG
            credential = new DefaultAzureCredential();
#else
            credential = new ManagedIdentityCredential();
#endif

            var subscriptionResourceId = SubscriptionResource.CreateResourceIdentifier(serviceConfiguration.SubscriptionId);
            _client = new ArmClient(credential, serviceConfiguration.SubscriptionId);
            _subscription = _client.GetSubscriptionResource(subscriptionResourceId);
            _dataResourceGroupName = serviceConfiguration.DataResourceGroup;
            _computeResourceGroupName = serviceConfiguration.ComputeResourceGroup;
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
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
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Started {webSite.Data.Name}"
                });
            }

            if (jobTrigger != null)
            {
                await jobTrigger.StartAsync(cancellationToken);
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Started {jobTrigger.Data.Name}"
                });
            }
        }

        public async Task StartWebSiteAsync(Guid requestorId, string websiteName, CancellationToken cancellationToken)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Starting {websiteName}..."
            });

            try
            {
                await GetResourceGroupsAsync(cancellationToken);
                var websiteResponse = _computeResourceGroup.GetWebSite(websiteName, cancellationToken);
                var website = websiteResponse.Value;

                var response = await website.StartAsync(cancellationToken);
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"{websiteName} responded with code: {response.Status}."
                });

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

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"{websiteName} is now {website.Data.State}."
                });

                // Give the website time to fully start
                await Task.Delay(10000, cancellationToken);
            }
            catch (Azure.RequestFailedException rex) when (rex.Status == 404)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"{websiteName} was not found."
                });
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Failed to start {websiteName}. {ex.Message}"
                });
            }

        }

        public async Task<Dictionary<string, string>> GetWebSitesStorageAccountsAsync(CancellationToken cancellationToken)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = "Getting storage account names..."
            });

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
                TokenCredential credential;
#if DEBUG
                credential = new DefaultAzureCredential();
#else
                credential = new ManagedIdentityCredential();
#endif

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
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = "Getting websites..."
            });

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
