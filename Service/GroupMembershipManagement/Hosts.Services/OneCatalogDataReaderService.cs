// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Repositories.Contracts;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Entities.ServiceBus;
using Entities;
using System.Configuration;
using Microsoft.Azure.Cosmos.Table;
using System.Linq;
using Repositories.Contracts.InjectConfig;
using Microsoft.Azure.Cosmos.Table.Queryable;
using Microsoft.Graph;
using Services.Contracts;
using System.Diagnostics.CodeAnalysis;

namespace Hosts.OneCatalogDataReaderApp
{
    
    public class OneCatalogDataReaderService : IOneCatalogDataReaderService
    {
        private IDataFactoryRepository _dataFactoryRepository = null;
        private ITableStorageRepository _tableStorageRepository = null;
        private IMembershipServiceBusRepository _membershipServiceBusRepositoryRepository = null;
        private readonly CloudTableClient _tableClient = null;
        private readonly CloudStorageAccount _cloudStorageAccount = null;
        private readonly IGraphService _graphService = null;

        public OneCatalogDataReaderService(IDataFactoryRepository dataFactoryRepository,
                                    ITableStorageRepository tableStorageRepository,
                                    IMembershipServiceBusRepository membershipServiceBusRepositoryRepository,
                                    IKeyVaultSecret<IOneCatalogDataReaderService> storageAccountName,
                                    IGraphService graphService)
        {
            _dataFactoryRepository = dataFactoryRepository;
            _tableStorageRepository = tableStorageRepository;
            _membershipServiceBusRepositoryRepository = membershipServiceBusRepositoryRepository;
            _cloudStorageAccount = new CloudStorageAccount(new StorageCredentials(_tableStorageRepository.GetAccountSASToken()), storageAccountName.Secret, null, true);
            _tableClient = _cloudStorageAccount.CreateCloudTableClient(new TableClientConfiguration());
            _graphService = graphService ?? throw new ArgumentNullException(nameof(graphService));

        }
        public async Task ReadDataAsync(SyncJob messageSent)
        {

            var runId = await _dataFactoryRepository.GetMostRecentSucceededRunIdAsync();
            var tableName = string.Concat("tbl", runId.Replace("-", ""));
            var table = _tableClient.GetTableReference(tableName);
                 
            var sastoken = _tableStorageRepository.GetAccountSASToken();

            var linqQuery = table.CreateQuery<PersonEntity>();
            linqQuery.FilterString = messageSent.Query;
            List<PersonEntity> odata = new List<PersonEntity>();

            if (!sastoken.Equals("sas-token")) {
                TableContinuationToken continuationToken = null;
                do
                {
                    var segmentResult = await table.ExecuteQuerySegmentedAsync(linqQuery.AsTableQuery(), continuationToken);
                    continuationToken = segmentResult.ContinuationToken;

                    if (odata.Count < 1000)
                        odata = segmentResult.Results;
                    else
                        odata.AddRange(segmentResult.Results);

                } while (continuationToken != null);
            }
            var groupMemberToBeSent = new GroupMembership();
            List<string> personnelNumbers = odata.Select(item => item.RowKey).ToList();
            
            var graphProfiles = await _graphService.GetAzureADObjectIds(personnelNumbers);
            Console.WriteLine($"\t{graphProfiles.Count} graph users retrieved...");
            
            foreach (var gp in graphProfiles)
            {
                groupMemberToBeSent.SourceMembers.Add(new AzureADUser { ObjectId = Guid.Parse(gp.Id) });
            }

            var group = new AzureADGroup();
            group.ObjectId = messageSent.TargetOfficeGroupId;
            groupMemberToBeSent.Destination = group;

            await _membershipServiceBusRepositoryRepository.SendMembership(groupMemberToBeSent, messageSent.Type);

        }

    }
}

