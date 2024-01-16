// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Models.Entities;
using Newtonsoft.Json;
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
        private readonly ITeamsChannelRepository _teamsChannelRepository;

        public DestinationAttributesUpdaterService(
            IDatabaseSyncJobsRepository databaseSyncJobsRepository, 
            IDatabaseDestinationAttributesRepository databaseDestinationAttributesRepository, 
            IGraphGroupRepository graphGroupRepository,
            ITeamsChannelRepository teamsChannelRepository)
        {
            _databaseSyncJobsRepository = databaseSyncJobsRepository ?? throw new ArgumentNullException(nameof(databaseSyncJobsRepository));
            _databaseDestinationAttributesRepository = databaseDestinationAttributesRepository;
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _teamsChannelRepository = teamsChannelRepository ?? throw new ArgumentNullException(nameof(teamsChannelRepository));
        }

        public async Task<List<(string Destination, Guid JobId)>> GetDestinationsAsync(string destinationType)
        {
            var jobs = await _databaseSyncJobsRepository.GetSyncJobsByDestinationAsync(destinationType);
            var destinations = new List<(string Destination, Guid JobId)>();

            foreach (var job in jobs)
            {
                var destinationToken = JArray.Parse(job.Destination).First();
                DestinationObject destination;
                 
                if (destinationType == "GroupMembership")
                {
                    destination = new DestinationObject
                    {
                        Type = destinationToken["type"].ToString(),
                        Value = new GroupDestinationValue() { ObjectId = Guid.Parse(destinationToken["value"]["objectId"].ToString()) }
                        
                    };
                }
                else if (destinationType == "TeamsChannelMembership")
                {
                    destination = new DestinationObject
                    {
                        Type = destinationToken["type"].ToString(),
                        Value = new TeamsChannelDestinationValue()
                        {
                            ObjectId = Guid.Parse(destinationToken["value"]["objectId"].ToString()),
                            ChannelId = destinationToken["value"]["channelId"].ToString()
                        }
                    };
                    
                }
                else
                {
                    continue;
                }

                var serializedDestination = JsonConvert.SerializeObject(destination, Formatting.Indented, new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.All
                });

                destinations.Add((serializedDestination, job.Id));
            }

            return destinations;
        }

        public async Task<List<DestinationAttributes>> GetBulkDestinationAttributesAsync(List<(string Destination, Guid JobId)> destinations, string destinationType)
        {
            
            var destinationAttributesList = new List<DestinationAttributes>();
            List<(DestinationObject? Destination, Guid JobId)> destinationObjectsMap = destinations
                .Select(d => (JsonConvert.DeserializeObject<DestinationObject>(d.Destination, new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.Auto
                }), d.JobId))
                .ToList();
            
            var destinationObjects = destinationObjectsMap.Select(d => d.Destination).ToList();

            if (destinationType == "GroupMembership") 
            {
                var names = new Dictionary<Guid, string>();
                var owners = new Dictionary<Guid, List<Guid>>();
                var destinationIdMap = new Dictionary<Guid, Guid>();

                foreach (var destination in destinationObjectsMap)
                {
                    if (!destinationIdMap.ContainsKey(destination.Destination.Value.ObjectId))
                        destinationIdMap.Add(destination.Destination.Value.ObjectId, destination.JobId);
                }

                var destinationGuids = destinationObjects.Select(d => d.Value.ObjectId).ToList();
                names = await _graphGroupRepository.GetGroupNamesAsync(destinationGuids);
                owners = await _graphGroupRepository.GetDestinationOwnersAsync(destinationGuids);

                foreach (var destination in destinationObjects)
                {
                    destinationAttributesList.Add(new DestinationAttributes
                    {
                        Name = names[destination.Value.ObjectId],
                        Owners = owners[destination.Value.ObjectId],
                        Id = destinationIdMap[destination.Value.ObjectId]
                    });
                }
            }
            else if (destinationType == "TeamsChannelMembership")
            {
                var names = new Dictionary<string, string>();
                var owners = new Dictionary<Guid, List<Guid>>();
                var channelDestinations = destinationObjects.Select((d) => { return new AzureADTeamsChannel() { ObjectId = d.Value.ObjectId, ChannelId = (d.Value as TeamsChannelDestinationValue).ChannelId }; }).ToList();
                var destinationIdMap = new Dictionary<string, Guid>();

                foreach (var destination in destinationObjectsMap)
                {
                    destinationIdMap.Add((destination.Destination.Value as TeamsChannelDestinationValue).ChannelId, destination.JobId);
                }

                var destinationGuids = destinationObjects.Select(d => d.Value.ObjectId).ToList();
                names = await _teamsChannelRepository.GetTeamsChannelNamesAsync(channelDestinations);
                owners = await _graphGroupRepository.GetDestinationOwnersAsync(destinationGuids);

                foreach (var destination in destinationObjects)
                {
                    destinationAttributesList.Add(new DestinationAttributes
                    {
                        Name = names[(destination.Value as TeamsChannelDestinationValue).ChannelId],
                        Owners = owners[destination.Value.ObjectId],
                        Id = destinationIdMap[(destination.Value as TeamsChannelDestinationValue).ChannelId]
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
