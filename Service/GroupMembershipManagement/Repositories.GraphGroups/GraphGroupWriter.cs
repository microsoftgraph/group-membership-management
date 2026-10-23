// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions;
using Models;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Group = Microsoft.Graph.Models.Group;

namespace Repositories.GraphGroups
{
    internal class GraphGroupWriter : GraphGroupRepositoryBase
    {
        private readonly GraphGroupInformationRepository _graphGroupInformationReader;
        private readonly ILogger<GraphGroupWriter> _graphGroupWriterLogger;

        public GraphGroupWriter(GraphServiceClient graphServiceClient,
                                GraphGroupMetricTracker graphGroupMetricTracker,
                                GraphGroupInformationRepository graphGroupInformationReader,
                                ILogger<GraphGroupWriter> graphGroupWriterLogger)
                                : base(graphServiceClient, graphGroupWriterLogger, graphGroupMetricTracker)
        {
            _graphGroupInformationReader = graphGroupInformationReader ?? throw new ArgumentNullException(nameof(graphGroupInformationReader));
            _graphGroupWriterLogger = graphGroupWriterLogger ?? throw new ArgumentNullException(nameof(graphGroupWriterLogger));
        }

        public async Task<AzureADGroup> CreateGroupAsync(string newGroupName, TestGroupType testGroupType, Guid? runId)
        {
            try
            {
                var exactMatchFilter = $"displayName eq '{newGroupName}'";
                var existingGroups = await _graphGroupInformationReader.GetGroupsByFilterAsync(exactMatchFilter);
                if (existingGroups.Any())
                {
                    return existingGroups.First();
                }

                var groupDefinition = new Group
                {
                    DisplayName = newGroupName,
                    MailEnabled = false,
                    SecurityEnabled = true
                };

                if (testGroupType == TestGroupType.IntegrationTesting)
                {
                    groupDefinition.Description = $"Integration test group: {newGroupName}";
                    groupDefinition.MailNickname = new Guid().ToString();
                }
                else
                {
                    groupDefinition.Description = $"Load test group: {newGroupName}";
                    groupDefinition.GroupTypes = new List<string> { "Unified" };
                    groupDefinition.MailNickname = Guid.NewGuid().ToString();
                }

                var group = await _graphServiceClient.Groups.PostAsync(groupDefinition);

                if (group == null)
                {
                    return null;
                }

                return new AzureADGroup { ObjectId = new Guid(group.Id) };
            }
            catch (ODataError ex)
            {
                _graphGroupWriterLogger.LogErrorWithRunId(runId, ex.GetBaseException().ToString(), ex);

                throw;
            }
            catch (Exception e)
            {
                _graphGroupWriterLogger.LogErrorWithRunId(runId, $"Error creating group: {e}", e);
                return null;
            }
        }

        public async Task AddGroupOwnersAsync(string groupId, List<Guid> ownerIds, Guid? runId)
        {
            if (ownerIds == null || !ownerIds.Any())
                return;

            _graphGroupWriterLogger.LogDebugWithRunId(runId, $"Adding {ownerIds.Count} owner(s) to group {groupId}.");

            int maxRetries = 10;
            foreach (var ownerId in ownerIds)
            {
                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        await _graphServiceClient.Groups[groupId].Owners.Ref.PostAsync(new ReferenceCreate
                        {
                            OdataId = $"https://graph.microsoft.com/v1.0/directoryObjects/{ownerId}"
                        });
                        break;
                    }
                    catch (ODataError odataEx) when (odataEx.ResponseStatusCode == (int)HttpStatusCode.NotFound && attempt < maxRetries)
                    {
                        var delay = Math.Min(5, (int)Math.Pow(2, attempt - 1));
                        _graphGroupWriterLogger.LogWarningWithRunId(runId,
                            $"Group {groupId} not found (attempt {attempt}/{maxRetries}). Retrying in {delay}s...");
                        await Task.Delay(TimeSpan.FromSeconds(delay));
                    }
                    catch (ODataError odataEx) when (odataEx.ResponseStatusCode == (int)HttpStatusCode.NotFound && attempt == maxRetries)
                    {
                        _graphGroupWriterLogger.LogErrorWithRunId(runId,
                            $"Group {groupId} not found after {maxRetries} retries. Giving up.", odataEx);
                        throw;
                    }
                    catch (ODataError odataEx) when (odataEx.ResponseStatusCode == (int)HttpStatusCode.BadRequest
                        && odataEx.Message != null
                        && odataEx.Message.Contains("object references already exist", StringComparison.OrdinalIgnoreCase))
                    {
                        _graphGroupWriterLogger.LogWarningWithRunId(runId,
                            $"Owner {ownerId} is already an owner of group {groupId}. Skipping.");
                        break;
                    }
                    catch (ODataError odataEx)
                    {
                        _graphGroupWriterLogger.LogErrorWithRunId(runId,
                            $"Unexpected ODataError adding owner {ownerId} to group {groupId} (HTTP {odataEx.ResponseStatusCode}): {odataEx.Message}", odataEx);
                        throw;
                    }
                }
            }

            _graphGroupWriterLogger.LogDebugWithRunId(runId, $"Finished adding owners to group {groupId}.");
        }

        public async Task<AzureADGroup> CreateGroupFromUIAsync(string newGroupName, Guid groupOwnerId, string newGroupAlias, string gmmOwnerObjectId, NewGroupType groupType, Guid? runId, string? description = null)
        {
            if (string.IsNullOrWhiteSpace(gmmOwnerObjectId))
            {
                // Creating the group without GMM as an owner would leave a group GMM cannot manage,
                // so fail before any Graph write rather than producing one.
                throw new ArgumentException("GMM's own directory object id is required to create a group.", nameof(gmmOwnerObjectId));
            }

            try
            {
                var isSecurityGroup = groupType == NewGroupType.Security;

                var groupDefinition = new Group
                {
                    DisplayName = newGroupName,
                    // A Microsoft 365 group is mail enabled and carries the Unified group type.
                    // A security group is neither: it is mail disabled with no group types at all.
                    // Graph requires a mail nickname either way.
                    MailEnabled = !isSecurityGroup,
                    SecurityEnabled = isSecurityGroup,
                    Description = description ?? newGroupName,
                    GroupTypes = isSecurityGroup ? new List<string>() : new List<string> { "Unified" },
                    MailNickname = MailNicknameBuilder.Build(newGroupAlias, newGroupName)
                };

                // The SDK has no typed property for owners@odata.bind and does not serialize the
                // Owners navigation property on create, so the binding goes through AdditionalData.
                var ownerBindings = new List<string>
                {
                    $"https://graph.microsoft.com/v1.0/directoryObjects/{groupOwnerId}"
                };

                var gmmOwnerBinding = $"https://graph.microsoft.com/v1.0/directoryObjects/{gmmOwnerObjectId}";

                // Graph rejects a repeated binding.
                if (!ownerBindings.Contains(gmmOwnerBinding, StringComparer.OrdinalIgnoreCase))
                {
                    ownerBindings.Add(gmmOwnerBinding);
                }

                groupDefinition.AdditionalData = new Dictionary<string, object>
                {
                    ["owners@odata.bind"] = ownerBindings
                };

                Group createdGroup = null;
                var createResponseHandler = new NativeResponseHandler();

                await _graphServiceClient.Groups.PostAsync(groupDefinition, requestConfiguration =>
                {
                    requestConfiguration.Options.Add(new ResponseHandlerOption { ResponseHandler = createResponseHandler });
                });

                var createResponse = createResponseHandler.Value as HttpResponseMessage;

                if (createResponse != null)
                {
                    var createHeaders = createResponse.Headers.ToImmutableDictionary(x => x.Key, x => x.Value);
                    await _graphGroupMetricTracker.TrackMetricsAsync(createHeaders, QueryType.Other, runId);

                    if (createResponse.IsSuccessStatusCode)
                    {
                        createdGroup = await DeserializeResponseAsync(createResponse, Group.CreateFromDiscriminatorValue);
                    }
                    else
                    {
                        throw new HttpRequestException($"Failed to create group. Status code: {createResponse.StatusCode}", null, createResponse.StatusCode);
                    }
                }

                if (createdGroup == null)
                {
                    return null;
                }

                var createdGroupId = new Guid(createdGroup.Id);

                // Graph has already confirmed the group exists by returning 201, but the new object is not
                // immediately readable from every replica. Reading it back is only a best-effort sanity check:
                // reporting failure here would leave a group behind that the caller believes was never created.
                if (!await WaitForGroupToBecomeReadableAsync(createdGroupId, runId))
                {
                    _graphGroupWriterLogger.LogWarningWithRunId(runId,
                        $"Group {createdGroupId} was created but is not readable yet after {GroupReadbackMaxAttempts} attempts. Continuing based on the successful create response.");
                }

                return new AzureADGroup
                {
                    ObjectId = createdGroupId,
                    Name = createdGroup.DisplayName
                };
            }
            catch (ODataError ex)
            {
                _graphGroupWriterLogger.LogErrorWithRunId(runId, ex.GetBaseException().ToString(), ex);

                throw new HttpRequestException("Group creation failed.", ex,
                    ex.ResponseStatusCode > 0 ? (HttpStatusCode)ex.ResponseStatusCode : null);
            }
            catch (HttpRequestException ex) when (ex.StatusCode.HasValue)
            {
                _graphGroupWriterLogger.LogErrorWithRunId(runId, ex.GetBaseException().ToString(), ex);
                throw;
            }
            catch (Exception e)
            {
                _graphGroupWriterLogger.LogErrorWithRunId(runId, $"Error creating group: {e}", e);
                return null;
            }
        }

        private const int GroupReadbackMaxAttempts = 5;
        private static readonly TimeSpan GroupReadbackInitialDelay = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan GroupReadbackMaxDelay = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Polls Graph until a freshly created group becomes readable, to absorb directory replication delay.
        /// Never throws: verification is advisory, so a transient read failure must not be mistaken for a
        /// failed creation.
        /// </summary>
        private async Task<bool> WaitForGroupToBecomeReadableAsync(Guid groupId, Guid? runId)
        {
            var delay = GroupReadbackInitialDelay;

            for (var attempt = 1; attempt <= GroupReadbackMaxAttempts; attempt++)
            {
                try
                {
                    if (await _graphGroupInformationReader.GroupExistsAsync(groupId, runId))
                    {
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _graphGroupWriterLogger.LogWarningWithRunId(runId,
                        $"Attempt {attempt} to read back group {groupId} failed: {ex.Message}", ex);
                }

                if (attempt < GroupReadbackMaxAttempts)
                {
                    await Task.Delay(delay);
                    delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, GroupReadbackMaxDelay.Ticks));
                }
            }

            return false;
        }
    }
}
