// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Models.Entities;
using Models.Helpers;
using Repositories.Contracts;
using Services.Contracts;
using System.Text.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Services
{
    public class DestinationAttributesUpdaterService : IDestinationAttributesUpdaterService
    {
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;
        private readonly IDatabaseGroupsRepository _databaseGroupsRepository;
        private readonly IDatabaseChannelsRepository _databaseChannelsRepository;
        private readonly IDatabaseDestinationAttributesRepository _databaseDestinationAttributesRepository;
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly ITeamsChannelRepository _teamsChannelRepository;
        private readonly JsonSerializerOptions _destinationObjectSerializerOptions;

        public DestinationAttributesUpdaterService(
            IDatabaseSyncJobsRepository databaseSyncJobsRepository,
            IDatabaseGroupsRepository databaseGroupsRepository,
            IDatabaseChannelsRepository databaseChannelsRepository,
            IDatabaseDestinationAttributesRepository databaseDestinationAttributesRepository,
            IGraphGroupRepository graphGroupRepository,
            ITeamsChannelRepository teamsChannelRepository)
        {
            _databaseSyncJobsRepository = databaseSyncJobsRepository ?? throw new ArgumentNullException(nameof(databaseSyncJobsRepository));
            _databaseGroupsRepository = databaseGroupsRepository ?? throw new ArgumentNullException(nameof(databaseGroupsRepository));
            _databaseChannelsRepository = databaseChannelsRepository ?? throw new ArgumentNullException(nameof(databaseChannelsRepository));
            _databaseDestinationAttributesRepository = databaseDestinationAttributesRepository;
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _teamsChannelRepository = teamsChannelRepository ?? throw new ArgumentNullException(nameof(teamsChannelRepository));
            _destinationObjectSerializerOptions = new JsonSerializerOptions { Converters = { new DestinationValueConverter() } };
        }

        public async Task<List<DestinationInfo>> GetDestinationsAsync(string destinationType)
        {
            var jobs = await _databaseSyncJobsRepository.GetSyncJobsByDestinationAsync(destinationType);
            var destinations = new List<DestinationInfo>();

            foreach (var job in jobs)
            {
                DestinationObject destination;
                 
                if (destinationType == "GroupMembership")
                {
                    var group = job.Group ?? await _databaseGroupsRepository.GetGroupUsingSyncJobIdAsync(job.Id);
                    if (group == null)
                    {
                        continue;
                    }

                    destination = new DestinationObject
                    {
                        Type = job.MembershipType.ToString(),
                        Value = new GroupDestinationValue() { ObjectId = group.GroupId }
                        
                    };
                }
                else if (destinationType == "TeamsChannelMembership")
                {
                    var channel = job.Channel ?? await _databaseChannelsRepository.GetChannelUsingSyncJobIdAsync(job.Id);
                    if (channel == null)
                    {
                        continue;
                    }

                    destination = new DestinationObject
                    {
                        Type = job.MembershipType.ToString(),
                        Value = new TeamsChannelDestinationValue()
                        {
                            ObjectId = channel.GroupId,
                            ChannelId = channel.ChannelId
                        }
                    };
                }
                else
                {
                    continue;
                }

                var serializedDestination = JsonSerializer.Serialize(destination, _destinationObjectSerializerOptions);

                destinations.Add(new DestinationInfo { Destination = serializedDestination, JobId = job.Id });
            }

            return destinations;
        }

        public async Task<List<DestinationAttributes>> GetBulkDestinationAttributesAsync(List<DestinationInfo> destinations, string destinationType)
        {
            
            var destinationAttributesList = new List<DestinationAttributes>();
            
            // Filter out null or empty destination strings before attempting to deserialize
            var validDestinations = destinations.Where(d => !string.IsNullOrWhiteSpace(d.Destination)).ToList();
            
            List<(DestinationObject? Destination, Guid JobId)> destinationObjectsMap = validDestinations
                .Select(d => (JsonSerializer.Deserialize<DestinationObject>(d.Destination, _destinationObjectSerializerOptions), d.JobId))
                .ToList();
            
            var destinationObjects = destinationObjectsMap.Select(d => d.Destination).ToList();

            if (destinationType == "GroupMembership") 
            {
                var names = new Dictionary<Guid, string>();
                var owners = new Dictionary<Guid, List<Guid>>();
                var emails = new Dictionary<Guid, string>();

                var destinationIdMap = new Dictionary<Guid, Guid>();

                foreach (var destination in destinationObjectsMap)
                {
                    if (!destinationIdMap.ContainsKey(destination.Destination.Value.ObjectId))
                        destinationIdMap.Add(destination.Destination.Value.ObjectId, destination.JobId);
                }

                var destinationGuids = destinationObjects.Select(d => d.Value.ObjectId).ToList();
                var groupDetails = await _graphGroupRepository.GetGroupsAsync(destinationGuids);
                names = groupDetails.ToDictionary(g => g.ObjectId, g => g.Name);
                emails = groupDetails.ToDictionary(g => g.ObjectId, g => g.Email);
                owners = await _graphGroupRepository.GetDestinationOwnersAsync(destinationGuids);


                foreach (var destination in destinationObjects)
                {
                    destinationAttributesList.Add(new DestinationAttributes
                    {
                        Name = names.ContainsKey(destination.Value.ObjectId) ? names[destination.Value.ObjectId] : null,
                        Owners = owners.ContainsKey(destination.Value.ObjectId) ? owners[destination.Value.ObjectId] : null,
                        Id = destinationIdMap[destination.Value.ObjectId],
                        Email = emails.ContainsKey(destination.Value.ObjectId) ? emails[destination.Value.ObjectId] : null
                    });
                }
            }
            else if (destinationType == "TeamsChannelMembership")
            {
                var names = new Dictionary<string, string>();
                var emails = new Dictionary<string, string>();
                var owners = new Dictionary<Guid, List<Guid>>();
                var channelDestinations = destinationObjects.Select((d) => { return new AzureADTeamsChannel() { ObjectId = d.Value.ObjectId, ChannelId = (d.Value as TeamsChannelDestinationValue).ChannelId }; }).ToList();
                var destinationIdMap = new Dictionary<string, Guid>();

                foreach (var destination in destinationObjectsMap)
                {
                    destinationIdMap.Add((destination.Destination.Value as TeamsChannelDestinationValue).ChannelId, destination.JobId);
                }

                var destinationGuids = destinationObjects.Select(d => d.Value.ObjectId).ToList();
                names = await _teamsChannelRepository.GetTeamsChannelNamesAsync(channelDestinations);
                emails = await _teamsChannelRepository.GetTeamsChannelEmailsAsync(channelDestinations);
                owners = await _graphGroupRepository.GetDestinationOwnersAsync(destinationGuids);

                foreach (var destination in destinationObjects)
                {
                    var channelValue = destination.Value as TeamsChannelDestinationValue;
                    var channelId = channelValue.ChannelId;     
                    var groupId = destination.Value.ObjectId;   

                    var channelEmail = emails.ContainsKey(channelId) 
                                        ? emails[channelId] 
                                        : null;

                    if (string.IsNullOrEmpty(channelEmail))
                    {
                        var mainChannel = await _teamsChannelRepository
                            .GetMainChannelAsync(groupId);
                        channelEmail = mainChannel?.Email;
                    }

                    var teamChannelId = (destination.Value as TeamsChannelDestinationValue).ChannelId;
                    destinationAttributesList.Add(new DestinationAttributes
                    {
                        Name = names.ContainsKey(teamChannelId) ? names[teamChannelId] : null,
                        Owners = owners.ContainsKey(destination.Value.ObjectId) ? owners[destination.Value.ObjectId] : null,
                        Id = destinationIdMap[teamChannelId],
                        Email = channelEmail
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
