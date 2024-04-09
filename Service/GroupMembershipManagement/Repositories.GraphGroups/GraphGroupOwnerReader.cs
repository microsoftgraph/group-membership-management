// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions;
using Models;
using Models.Entities;
using Repositories.Contracts;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using static Microsoft.Graph.CoreConstants;

namespace Repositories.GraphGroups
{
    internal class GraphGroupOwnerReader : GraphGroupRepositoryBase
    {
        public GraphGroupOwnerReader(GraphServiceClient graphServiceClient,
                                      ILoggingRepository loggingRepository,
                                      GraphGroupMetricTracker graphGroupMetricTracker)
                                      : base(graphServiceClient, loggingRepository, graphGroupMetricTracker)
        { }

        public async Task<bool> IsAppIDOwnerOfGroupAsync(string appId, Guid groupObjectId, Guid? runId)
        {
            // get the service principal ID by its app ID
            var nativeResponseHandler = new NativeResponseHandler();
            var response = new DirectoryObjectCollectionResponse();

            IDictionary<string, IEnumerable<string>> headers = null;

            await _graphServiceClient.ServicePrincipals
                                        .GetAsync(requestConfiguration =>
                                        {
                                            requestConfiguration
                                                .QueryParameters
                                                .Filter = $"appId eq '{appId}'";

                                            requestConfiguration
                                                .Options
                                                .Add(new ResponseHandlerOption { ResponseHandler = nativeResponseHandler });
                                        });

            var nativeResponse = nativeResponseHandler.Value as HttpResponseMessage;

            if (nativeResponse.IsSuccessStatusCode)
            {
                response = await DeserializeResponseAsync(nativeResponse,
                                                          DirectoryObjectCollectionResponse.CreateFromDiscriminatorValue);

                headers = nativeResponse.Headers.ToImmutableDictionary(x => x.Key, x => x.Value);

                if (!response.Value.Any())
                    return false;

                var servicePrincipal = response.Value.First();

                await _graphGroupMetricTracker.TrackMetricsAsync(headers, QueryType.Other, runId);

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = runId,
                    Message = $"Checking if app ID {appId} (service principal with object ID {servicePrincipal.Id}) owns the group {groupObjectId}."
                });

                return await IsGroupOwnerAsync($"id eq '{servicePrincipal.Id}'", groupObjectId, runId);
            }

            return false;
        }

        public async Task<bool> IsServiceAccountOwnerOfGroupAsync(Guid serviceAccountObjectId, Guid groupObjectId, Guid? runId)
        {
            return await IsGroupOwnerAsync($"id eq '{serviceAccountObjectId}'", groupObjectId, runId);
        }

        public async Task<List<AzureADUser>> GetGroupOwnersAsync(Guid groupObjectId, Guid? runId, int top = 0)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                RunId = runId,
                Message = $"Getting owners of group {groupObjectId}."
            });

            var owners = new List<User>();

            try
            {
                var nativeResponseHandler = new NativeResponseHandler();
                var groupOwnersResponse = new DirectoryObjectCollectionResponse();

                await _graphServiceClient.Groups[groupObjectId.ToString()].Owners.GetAsync(requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.Top = top > 0 ? top : null;
                    requestConfiguration.Options.Add(new ResponseHandlerOption { ResponseHandler = nativeResponseHandler });
                });

                var nativeResponse = nativeResponseHandler.Value as HttpResponseMessage;

                if (nativeResponse.IsSuccessStatusCode)
                {
                    groupOwnersResponse = await DeserializeResponseAsync(nativeResponse,
                                                                         DirectoryObjectCollectionResponse.CreateFromDiscriminatorValue);

                    var headers = nativeResponse.Headers.ToImmutableDictionary(x => x.Key, x => x.Value);

                    owners.AddRange(groupOwnersResponse.Value.OfType<User>());
                    await _graphGroupMetricTracker.TrackMetricsAsync(headers, QueryType.Other, runId);

                    await _loggingRepository.LogMessageAsync(new LogMessage
                    {
                        RunId = runId,
                        Message = $"Retrieved{(top > 0 ? " top " : " ")}{owners.Count} owners of group {groupObjectId}."
                    });

                    return owners.Select(x => new AzureADUser
                    {
                        ObjectId = Guid.Parse(x.Id),
                        Mail = x.Mail
                    })
                    .ToList();
                }

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = runId,
                    Message = $"Failed to retrieve owners of group {groupObjectId}. StatusCode {nativeResponse.StatusCode}"
                });

                return new List<AzureADUser>();

            }
            catch (ODataError ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = ex.GetBaseException().ToString(),
                    RunId = runId
                });

                throw;
            }
        }

        public async Task<bool> IsEmailRecipientOwnerOfGroupAsync(string userIdentifier, Guid groupObjectId, Guid? runId)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                RunId = runId,
                Message = $"Checking on user existence for user identifier '{userIdentifier}' to determine if it is an owner of group {groupObjectId}."
            });

            Guid userId;

            var identifierIsObjectId = Guid.TryParse(userIdentifier, out userId);

            if (!identifierIsObjectId)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = runId,
                    Message = $"Getting user information for email '{userIdentifier}'."
                });

                User user = null;
                var nativeResponseHandler = new NativeResponseHandler();
                var userByMailResponse = new UserCollectionResponse();

                try
                {

                    await _graphServiceClient.Users.GetAsync(requestConfiguration =>
                    {
                        requestConfiguration.QueryParameters.Filter = $"Mail eq '{userIdentifier}' or UserPrincipalName eq '{userIdentifier}'";
                        requestConfiguration.Options.Add(new ResponseHandlerOption { ResponseHandler = nativeResponseHandler });
                    });

                    var nativeResponse = nativeResponseHandler.Value as HttpResponseMessage;

                    if (nativeResponse.IsSuccessStatusCode)
                    {
                        userByMailResponse = await DeserializeResponseAsync(nativeResponse, UserCollectionResponse.CreateFromDiscriminatorValue);

                        if (userByMailResponse.Value.Count == 0)
                        {
                            await _loggingRepository.LogMessageAsync(new LogMessage
                            {
                                RunId = runId,
                                Message = $"No user was found when checking for user with email {userIdentifier}."
                            });

                            return false;
                        }

                        user = userByMailResponse.Value[0];
                        Guid.TryParse(user.Id, out userId);
                    }
                    else
                    {
                        throw new Exception($"Unable to verify group ownership at this time.");
                    }

                }
                catch (Exception ex)
                {
                    await _loggingRepository.LogMessageAsync(new LogMessage
                    {
                        Message = ex.GetBaseException().ToString(),
                        RunId = runId
                    });

                    throw;
                }
            }
            else
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = runId,
                    Message = $"User identifier is already an object id: '{userId}'."
                });
            }

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                RunId = runId,
                Message = $"Checking if email recipient owns the group {groupObjectId}."
            });

            return await IsGroupOwnerAsync($"id eq '{userId}'", groupObjectId, runId);

        }

        private async Task<bool> IsGroupOwnerAsync(string query, Guid groupObjectId, Guid? runId)
        {
            try
            {
                var nativeResponseHandler = new NativeResponseHandler();
                var groupOwnersResponse = new DirectoryObjectCollectionResponse();

                await _graphServiceClient.Groups[groupObjectId.ToString()]
                                            .Owners.GetAsync(requestConfiguration =>
                                            {
                                                requestConfiguration.QueryParameters.Filter = query;
                                                requestConfiguration.Options.Add(new ResponseHandlerOption { ResponseHandler = nativeResponseHandler });
                                            });

                var nativeResponse = nativeResponseHandler.Value as HttpResponseMessage;

                if (nativeResponse.IsSuccessStatusCode)
                {
                    groupOwnersResponse = await DeserializeResponseAsync(nativeResponse, DirectoryObjectCollectionResponse.CreateFromDiscriminatorValue);
                }

                var headers = nativeResponse.Headers.ToImmutableDictionary(x => x.Key, x => x.Value);
                await _graphGroupMetricTracker.TrackMetricsAsync(headers, QueryType.Other, runId);

                return groupOwnersResponse.Value?.Any() ?? false;
            }
            catch (ODataError ex)
            {
                if (ex.ResponseStatusCode == (int)HttpStatusCode.NotFound)
                    return false;

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = ex.GetBaseException().ToString(),
                    RunId = runId
                });

                throw;
            }
        }

    }
}
