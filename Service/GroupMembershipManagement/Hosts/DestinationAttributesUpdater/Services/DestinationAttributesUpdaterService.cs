// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Models.Entities;
using Newtonsoft.Json.Linq;
using Repositories.Contracts;
using Services.Contracts;

namespace Services
{
    public class DestinationAttributesUpdaterService : IDestinationAttributesUpdaterService
    {
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;
        private readonly IDatabaseDestinationAttributesRepository _databaseDestinationAttributesRepository;
        private readonly IGraphGroupRepository _graphGroupRepository;

        public DestinationAttributesUpdaterService(IDatabaseSyncJobsRepository databaseSyncJobsRepository, IDatabaseDestinationAttributesRepository databaseDestinationAttributesRepository, IGraphGroupRepository graphGroupRepository)
        {
            _databaseSyncJobsRepository = databaseSyncJobsRepository ?? throw new ArgumentNullException(nameof(databaseSyncJobsRepository));
            _databaseDestinationAttributesRepository = databaseDestinationAttributesRepository;
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
        }

        public async Task<List<(AzureADGroup Destination, Guid JobId)>> GetDestinationsAsync(string destinationType)
        {
            var jobs = await _databaseSyncJobsRepository.GetSyncJobsByDestinationAsync(destinationType);
            var destinations = new List<(AzureADGroup Destination, Guid JobId)>();

            foreach (var job in jobs)
            {
                var destinationToken = JArray.Parse(job.Destination).First();
                AzureADGroup destination;
                 
                if (destinationType == "GroupMembership")
                {
                    destination = new AzureADGroup
                    {
                        Type = destinationToken["type"].ToString(),
                        ObjectId = Guid.Parse(destinationToken["value"]["objectId"].ToString())
                    };
                    destinations.Add((destination, job.Id));
                }
                else if (destinationType == "TeamsChannelMembership")
                {
                    destination = new AzureADTeamsChannel
                    {
                        Type = destinationToken["type"].ToString(),
                        ObjectId = Guid.Parse(destinationToken["value"]["objectId"].ToString()),
                        ChannelId = destinationToken["value"]["channelId"].ToString()
                    };
                    destinations.Add((destination, job.Id));
                }
            }

            return destinations;
        }

        public async Task<List<DestinationAttributes>> GetBulkDestinationAttributesAsync(List<(AzureADGroup Destination, Guid JobId)> destinations, string destinationType)
        {
            
            var destinationAttributesList = new List<DestinationAttributes>();
            var destinationObjects = destinations.Select(d => d.Destination).ToList();

            if (destinationType == "GroupMembership") 
            {
                var names = new Dictionary<Guid, string>();
                var owners = new Dictionary<Guid, List<Guid>>();
                var destinationIdMap = new Dictionary<Guid, Guid>();

                foreach (var destination in destinations)
                {
                    if (!destinationIdMap.ContainsKey(destination.Destination.ObjectId))
                        destinationIdMap.Add(destination.Destination.ObjectId, destination.JobId);
                }

                var destinationGuids = destinationObjects.Select(d => d.ObjectId).ToList();
                names = await _graphGroupRepository.GetGroupNamesAsync(destinationGuids);
                owners = await _graphGroupRepository.GetDestinationOwnersAsync(destinationGuids);

                foreach (var destination in destinationObjects)
                {
                    destinationAttributesList.Add(new DestinationAttributes
                    {
                        Name = names[destination.ObjectId],
                        Owners = owners[destination.ObjectId],
                        Id = destinationIdMap[destination.ObjectId]
                    });
                }
            }
            else if (destinationType == "TeamsChannelMembership")
            {
                var names = new Dictionary<string, string>();
                var owners = new Dictionary<Guid, List<Guid>>();
                var teamsDestinations = destinationObjects.Select(d => d as AzureADTeamsChannel).ToList();
                var destinationIdMap = new Dictionary<string, Guid>();

                foreach (var destination in destinations)
                {
                    destinationIdMap.Add((destination.Destination as AzureADTeamsChannel).ChannelId, destination.JobId);
                }

                var destinationGuids = destinationObjects.Select(d => d.ObjectId).ToList();
                names = await _graphGroupRepository.GetTeamsChannelsNamesAsync(teamsDestinations);
                owners = await _graphGroupRepository.GetDestinationOwnersAsync(destinationGuids);

                foreach (var destination in destinationObjects)
                {
                    destinationAttributesList.Add(new DestinationAttributes
                    {
                        Name = names[(destination as AzureADTeamsChannel).ChannelId],
                        Owners = owners[destination.ObjectId],
                        Id = destinationIdMap[(destination as AzureADTeamsChannel).ChannelId]
                    });
                }
            }

            return destinationAttributesList;
        }

        public async Task UpdateAttributes(DestinationAttributes destinationAttributes)
        {
            await _databaseDestinationAttributesRepository.UpdateAttributes(destinationAttributes);
        }

    }
}
