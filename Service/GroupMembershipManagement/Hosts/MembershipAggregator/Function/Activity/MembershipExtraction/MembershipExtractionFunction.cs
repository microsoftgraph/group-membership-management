// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Models.Helpers;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Models.ServiceBus;

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

        [FunctionName(nameof(MembershipExtractionFunction))]
        public async Task<MembershipExtractionResponse> ExtractMembershipAsync([ActivityTrigger] MembershipExtractionRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage 
            { 
                Message = $"Extracting membership information for {request.CompletedParts.Count} parts", 
                RunId = request.SyncJob.RunId 
            }, VerbosityLevel.DEBUG);

            try
            {
                var allGroupMemberships = new List<(string FilePath, string Content)>();

                // Download all files sequentially to control memory usage
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

                // Process the membership data
                var membershipResult = await ExtractMembershipInformation(
                    allGroupMemberships.ToArray(), request.DestinationPart, request.SyncJob.RunId ?? Guid.Empty);

                if (membershipResult.SourceMembership == null)
                {
                    return new MembershipExtractionResponse
                    {
                        IsSuccessful = false,
                        ErrorMessage = "SourceMembership could not be extracted"
                    };
                }

                bool destinationExpected = request.CompletedParts.Contains(request.DestinationPart);
                if (destinationExpected && membershipResult.DestinationMembership == null)
                {
                    return new MembershipExtractionResponse
                    {
                        IsSuccessful = false,
                        ErrorMessage = "DestinationMembership could not be extracted"
                    };
                }

                var destinationMemberCount = membershipResult.DestinationMembership?.SourceMembers.Count ?? 0;
                await _loggingRepository.LogMessageAsync(new LogMessage 
                { 
                    Message = $"Successfully extracted membership information with {membershipResult.SourceMembership.SourceMembers.Count} source members and {destinationMemberCount} destination members", 
                    RunId = request.SyncJob.RunId 
                }, VerbosityLevel.DEBUG);

                return new MembershipExtractionResponse
                {
                    IsSuccessful = true,
                    SourceMembership = membershipResult.SourceMembership,
                    DestinationMembership = membershipResult.DestinationMembership
                };
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage 
                { 
                    Message = $"Error extracting membership information: {ex.Message}", 
                    RunId = request.SyncJob.RunId 
                }, VerbosityLevel.DEBUG);

                return new MembershipExtractionResponse
                {
                    IsSuccessful = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private async Task<(GroupMembership SourceMembership, GroupMembership DestinationMembership)>
            ExtractMembershipInformation((string FilePath, string Content)[] allGroupMemberships, string destinationPath, Guid runId)
        {
            var sourceGroupsMemberships = new List<GroupMembership>();
            
            // Process source memberships with error handling
            foreach (var membership in allGroupMemberships.Where(x => x.FilePath != destinationPath))
            {
                try
                {
                    if (string.IsNullOrEmpty(membership.Content))
                    {
                        throw new InvalidOperationException($"Content for file '{membership.FilePath}' is null or empty");
                    }

                    // Try to handle both compressed and uncompressed content
                    // Small groups (< 100K members) are stored as raw JSON
                    // Large groups (≥ 100K members) are stored as compressed JSON
                    string jsonContent;
                    try
                    {
                        // First, try to decompress as Base-64 compressed content
                        jsonContent = TextCompressor.Decompress(membership.Content);
                    }
                    catch (FormatException)
                    {
                        // If decompression fails, assume it's raw JSON content
                        jsonContent = membership.Content;
                    }

                    var groupMembership = JsonSerializer.Deserialize<GroupMembership>(jsonContent);
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
                               .Select(u => new AzureADUser() { ObjectId = u.Key, SourceGroups = u.Select(y => y.SourceGroup).Distinct().ToList() })
                               .ToList();

            var objectIds = new HashSet<Guid>(diff.Select(u => u.ObjectId));
            var sourceMembers = listGrouped.Where(u => objectIds.Contains(u.ObjectId)).ToList();

            sourceGroupMembership.SourceMembers = sourceMembers;

            var destinationMembershipFile = allGroupMemberships.FirstOrDefault(x => x.FilePath == destinationPath);
            if (destinationMembershipFile.FilePath == null)
            {
                return (sourceGroupMembership, null);
            }

            GroupMembership destinationGroupMembership;
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

                // Try to handle both compressed and uncompressed content
                // Small groups (< 100K members) are stored as raw JSON
                // Large groups (≥ 100K members) are stored as compressed JSON
                string jsonContent;
                try
                {
                    // First, try to decompress as Base-64 compressed content
                    jsonContent = TextCompressor.Decompress(destinationMembershipFile.Content);
                }
                catch (FormatException)
                {
                    // If decompression fails, assume it's raw JSON content
                    jsonContent = destinationMembershipFile.Content;
                }

                destinationGroupMembership = JsonSerializer.Deserialize<GroupMembership>(jsonContent);
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

            return (sourceGroupMembership, destinationGroupMembership);
        }
    }
}
