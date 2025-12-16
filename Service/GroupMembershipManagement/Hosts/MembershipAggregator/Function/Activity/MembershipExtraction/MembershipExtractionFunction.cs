// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.MembershipAggregator.Helpers;
using Microsoft.Azure.Functions.Worker;
using Models;
using Models.Helpers;
using Models.ServiceBus;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public class MembershipExtractionFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IBlobStorageRepository _blobStorageRepository;

        public MembershipExtractionFunction(ILoggingRepository loggingRepository, IBlobStorageRepository blobStorageRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
        }

        [Function(nameof(MembershipExtractionFunction))]
        public async Task<MembershipExtractionResponse> ExtractMembershipAsync([ActivityTrigger] MembershipExtractionRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Extracting membership information for {request.CompletedParts.Count} parts",
                RunId = request.SyncJob?.RunId
            }, VerbosityLevel.DEBUG);

            try
            {
                var allGroupMemberships = new List<(string FilePath, string Content)>();

                foreach (var part in request.CompletedParts)
                {
                    var blobResult = await _blobStorageRepository.DownloadFileAsync(part);
                    if (blobResult.BlobStatus == BlobStatus.NotFound)
                    {
                        return new MembershipExtractionResponse
                        {
                            IsSuccessful = false,
                            ErrorMessage = $"File {part} was not found"
                        };
                    }

                    allGroupMemberships.Add((part, blobResult.Content));
                }

                var membershipResult = await ExtractMembershipInformationAsync(
                    allGroupMemberships.ToArray(),
                    request.DestinationPart,
                    request.SyncJob?.RunId ?? Guid.Empty);

                if (membershipResult.SourceMembership == null)
                {
                    return new MembershipExtractionResponse
                    {
                        IsSuccessful = false,
                        ErrorMessage = "SourceMembership could not be extracted"
                    };
                }

                var destinationExpected = !string.IsNullOrWhiteSpace(request.DestinationPart) && request.CompletedParts.Contains(request.DestinationPart);
                if (destinationExpected && membershipResult.DestinationMembership == null)
                {
                    return new MembershipExtractionResponse
                    {
                        IsSuccessful = false,
                        ErrorMessage = "DestinationMembership could not be extracted"
                    };
                }

                var destinationMemberCount = membershipResult.DestinationMembership?.SourceMembers?.Count ?? 0;

                var currentUtcDateTime = request.CurrentUtcDateTime == default ? DateTime.UtcNow : request.CurrentUtcDateTime;
                var groupId = request.GroupId != Guid.Empty
                               ? request.GroupId
                               : request.SyncJob?.TargetOfficeGroupId ?? Guid.Empty;

                if (groupId == Guid.Empty)
                {
                    throw new InvalidOperationException("Unable to determine a valid group identifier for membership extraction output.");
                }

                var serializerOptions = new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
                };

                var sourceFilePath = MembershipFilePathHelper.BuildFilePath(request.SyncJob, groupId, "SourceMembership", currentUtcDateTime);
                var sourceContent = TextCompressor.Compress(JsonSerializer.Serialize(membershipResult.SourceMembership, serializerOptions));
                await _blobStorageRepository.UploadFileAsync(sourceFilePath, sourceContent);

                string destinationFilePath = null;
                if (membershipResult.DestinationMembership != null)
                {
                    destinationFilePath = MembershipFilePathHelper.BuildFilePath(request.SyncJob, groupId, "DestinationMembership", currentUtcDateTime);
                    var destinationContent = TextCompressor.Compress(JsonSerializer.Serialize(membershipResult.DestinationMembership, serializerOptions));
                    await _blobStorageRepository.UploadFileAsync(destinationFilePath, destinationContent);
                }

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Successfully extracted membership information with {membershipResult.SourceMembership.SourceMembers.Count} source members and {destinationMemberCount} destination members",
                    RunId = request.SyncJob?.RunId
                }, VerbosityLevel.DEBUG);

                return new MembershipExtractionResponse
                {
                    IsSuccessful = true,
                    SourceMembership = membershipResult.SourceMembership,
                    DestinationMembership = membershipResult.DestinationMembership,
                    SourceMembershipFilePath = sourceFilePath,
                    DestinationMembershipFilePath = destinationFilePath,
                    SourceMemberCount = membershipResult.SourceMembership.SourceMembers.Count,
                    DestinationMemberCount = destinationMemberCount
                };
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Error extracting membership information: {ex.Message}",
                    RunId = request.SyncJob?.RunId
                }, VerbosityLevel.DEBUG);

                return new MembershipExtractionResponse
                {
                    IsSuccessful = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private async Task<(GroupMembership SourceMembership, GroupMembership DestinationMembership)> ExtractMembershipInformationAsync(
            (string FilePath, string Content)[] allGroupMemberships,
            string destinationPath,
            Guid runId)
        {
            var sourceGroupsMemberships = new List<GroupMembership>();

            foreach (var membership in allGroupMemberships.Where(x => x.FilePath != destinationPath))
            {
                try
                {
                    if (string.IsNullOrEmpty(membership.Content))
                    {
                        throw new InvalidOperationException($"Content for file '{membership.FilePath}' is null or empty");
                    }

                    var jsonContent = TryDecompress(membership.Content);
                    var groupMembership = JsonSerializer.Deserialize<GroupMembership>(jsonContent);
                    if (groupMembership == null)
                    {
                        throw new InvalidOperationException($"Deserialization of GroupMembership from file '{membership.FilePath}' returned null");
                    }

                    sourceGroupsMemberships.Add(groupMembership);
                }
                catch (FormatException ex)
                {
                    await _loggingRepository.LogMessageAsync(new LogMessage
                    {
                        Message = $"Failed to process file '{membership.FilePath}': {ex.Message}",
                        RunId = runId
                    }, VerbosityLevel.INFO);
                    throw new InvalidOperationException($"Failed to process content from file '{membership.FilePath}': {ex.Message}", ex);
                }
                catch (JsonException ex)
                {
                    await _loggingRepository.LogMessageAsync(new LogMessage
                    {
                        Message = $"JSON deserialization failed for file '{membership.FilePath}': {ex.Message}",
                        RunId = runId
                    }, VerbosityLevel.INFO);
                    throw new InvalidOperationException($"Failed to deserialize JSON content from file '{membership.FilePath}': {ex.Message}", ex);
                }
            }

            if (sourceGroupsMemberships.Count == 0)
            {
                return (null, null);
            }

            var sourceGroupMembership = sourceGroupsMemberships[0];
            var toInclude = sourceGroupsMemberships.Where(g => !g.Exclusionary).SelectMany(x => x.SourceMembers).ToList();
            var toExclude = sourceGroupsMemberships.Where(g => g.Exclusionary).SelectMany(x => x.SourceMembers).ToList();
            var diff = toInclude.Except(toExclude).ToList();

            var source = sourceGroupsMemberships.SelectMany(x => x.SourceMembers).ToList();
            var listGrouped = source.GroupBy(u => u.ObjectId)
                               .Select(u => new AzureADUser { ObjectId = u.Key, SourceGroups = u.Select(y => y.SourceGroup).Distinct().ToList() })
                               .ToList();

            var objectIds = new HashSet<Guid>(diff.Select(u => u.ObjectId));
            var sourceMembers = listGrouped.Where(u => objectIds.Contains(u.ObjectId)).ToList();

            sourceGroupMembership.SourceMembers = sourceMembers;

            var destinationMembershipFile = allGroupMemberships.FirstOrDefault(x => x.FilePath == destinationPath);
            if (string.IsNullOrEmpty(destinationMembershipFile.FilePath))
            {
                return (sourceGroupMembership, null);
            }

            try
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Processing destination membership file: {destinationPath}, Content length: {destinationMembershipFile.Content?.Length ?? 0}",
                    RunId = runId
                }, VerbosityLevel.DEBUG);

                if (string.IsNullOrEmpty(destinationMembershipFile.Content))
                {
                    throw new InvalidOperationException($"Content for destination file '{destinationPath}' is null or empty");
                }

                var jsonContent = TryDecompress(destinationMembershipFile.Content);
                var destinationGroupMembership = JsonSerializer.Deserialize<GroupMembership>(jsonContent);
                if (destinationGroupMembership == null)
                {
                    throw new InvalidOperationException($"Deserialization of destination GroupMembership from file '{destinationPath}' returned null");
                }

                return (sourceGroupMembership, destinationGroupMembership);
            }
            catch (FormatException ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Failed to process destination file '{destinationPath}': {ex.Message}",
                    RunId = runId
                }, VerbosityLevel.INFO);
                throw new InvalidOperationException($"Failed to process content from destination file '{destinationPath}': {ex.Message}", ex);
            }
            catch (JsonException ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"JSON deserialization failed for destination file '{destinationPath}': {ex.Message}",
                    RunId = runId
                }, VerbosityLevel.INFO);
                throw new InvalidOperationException($"Failed to deserialize JSON content from destination file '{destinationPath}': {ex.Message}", ex);
            }
        }

        private static string TryDecompress(string content)
        {
            try
            {
                return TextCompressor.Decompress(content);
            }
            catch (FormatException)
            {
                return content;
            }
        }
    }
}
