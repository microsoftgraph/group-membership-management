// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.DataFactory;
using Azure.ResourceManager.DataFactory.Models;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Repositories.DataFactory
{
    public class DataFactoryRepository : IDataFactoryRepository
    {
        private readonly string _pipeline = null;
        private readonly string _dataFactory = null;
        private readonly string _subscriptionId = null;
        private readonly string _resourceGroup = null;
        private readonly ArmClient _client = null;
        private readonly ResourceIdentifier _dataFactoryResourceId = null;

        public DataFactoryRepository(IDataFactorySecret<IDataFactoryRepository> dataFactorySecrets)
        {
            _pipeline = dataFactorySecrets.Pipeline;
            _dataFactory = dataFactorySecrets.DataFactoryName;
            _subscriptionId = dataFactorySecrets.SubscriptionId;
            _resourceGroup = dataFactorySecrets.ResourceGroup;

            TokenCredential credential;
#if DEBUG
            credential = new DefaultAzureCredential();
#else
            credential = new ManagedIdentityCredential();
#endif

            _client = new ArmClient(credential);
            _dataFactoryResourceId = DataFactoryResource.CreateResourceIdentifier(_subscriptionId, _resourceGroup, _dataFactory);
        }

        public async Task<string> GetMostRecentSucceededRunIdAsync()
        {
            var pipelineResponse = await GetDataFactoryPipelineRunsAsync();
            return pipelineResponse.Count > 0 ? pipelineResponse[0].RunId?.ToString() : null;
        }

        public async Task<(string latest, string previous)> GetTwoRecentSucceededRunIdsAsync()
        {
            var pipelineResponse = await GetDataFactoryPipelineRunsAsync();
            return (pipelineResponse?.Count >= 2) ? (pipelineResponse[0]?.RunId?.ToString(), pipelineResponse[1]?.RunId?.ToString()) : (null, null);
        }

        private async Task<List<DataFactoryPipelineRunInfo>> GetDataFactoryPipelineRunsAsync()
        {
            var dataFactory = _client.GetDataFactoryResource(_dataFactoryResourceId);
            RunFilterContent content = new RunFilterContent(DateTime.UtcNow.AddMonths(-1), DateTime.UtcNow)
            {
                Filters =
                {
                    new RunQueryFilter(RunQueryFilterOperand.PipelineName, RunQueryFilterOperator.EqualsValue, new string[] { _pipeline }),
                    new RunQueryFilter(RunQueryFilterOperand.Status, RunQueryFilterOperator.EqualsValue, new string[] { "Succeeded" })
                }
            };

            var pipelineRuns = new List<DataFactoryPipelineRunInfo>();
            await foreach (DataFactoryPipelineRunInfo item in dataFactory.GetPipelineRunsAsync(content))
            {
                pipelineRuns.Add(item);
            }

            return pipelineRuns.OrderByDescending(x => x.RunEndOn).ToList();
        }
    }
}
