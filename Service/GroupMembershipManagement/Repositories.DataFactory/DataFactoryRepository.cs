// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Identity;
using Microsoft.Azure.Management.DataFactory;
using Microsoft.Azure.Management.DataFactory.Models;
using Microsoft.Identity.Client;
using Microsoft.Rest;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.DataFactory
{
    public class DataFactoryRepository : IDataFactoryRepository
    {
        private readonly string _pipeline = null;
        private readonly string _dataFactory = null;
        private readonly string _subscriptionId = null;
        private readonly string _resourceGroup = null;

        public DataFactoryRepository(IDataFactorySecret<IDataFactoryRepository> dataFactorySecrets)
        {
            _pipeline = dataFactorySecrets.Pipeline;
            _dataFactory = dataFactorySecrets.DataFactoryName;
            _subscriptionId = dataFactorySecrets.SubscriptionId;
            _resourceGroup = dataFactorySecrets.ResourceGroup;
        }

        public async Task<string> GetMostRecentSucceededRunIdAsync()
        {
            var pipelineResponse = await GetDataFactoryPipelineRunsAsync();
            return pipelineResponse?.Value?.Count > 0 ? pipelineResponse?.Value?[0]?.RunId : null;
        }

        public async Task<(string latest, string previous)> GetTwoRecentSucceededRunIdsAsync()
        {
            var pipelineResponse = await GetDataFactoryPipelineRunsAsync();
            return (pipelineResponse?.Value?.Count >= 2) ? (pipelineResponse?.Value?[0]?.RunId, pipelineResponse?.Value?[1]?.RunId) : (null, null);
        }

        private async Task<PipelineRunsQueryResponse> GetDataFactoryPipelineRunsAsync()
        {
            var credentials = await GetCredentialsAsync();
            var client = new DataFactoryManagementClient(credentials)
            {
                SubscriptionId = _subscriptionId
            };

            var pipeline = new RunQueryFilter("PipelineName", "Equals", new List<string> { _pipeline });
            var status = new RunQueryFilter("Status", "Equals", new List<string> { "Succeeded" });
            var pipelineRuns = new RunQueryOrderBy("RunEnd", "DESC");
            var before = DateTime.UtcNow;
            var after = before.AddMonths(-1);
            var param = new RunFilterParameters(after, before, null, new List<RunQueryFilter> { pipeline, status }, new List<RunQueryOrderBy> { pipelineRuns });
            var pipelineResponse = await client.PipelineRuns.QueryByFactoryAsync(
                                                            _resourceGroup,
                                                            _dataFactory, param);
            return pipelineResponse;
        }

        private async Task<TokenCredentials> GetCredentialsAsync()
        {
            var defaultAzureCredential = new DefaultAzureCredential(); 
            var tokenRequestContext = new Azure.Core.TokenRequestContext(new[] { "https://management.azure.com/.default" });
            var accessToken = await defaultAzureCredential.GetTokenAsync(tokenRequestContext);
            var tokenCredentials = new TokenCredentials(accessToken.Token);
            return tokenCredentials;
        }
    }
}
