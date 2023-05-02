// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Models;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

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
            var servicePrincipalResponse = await _graphServiceClient.ServicePrincipals
                                                            .GetAsync(requestConfiguration =>
                                                            {
                                                                requestConfiguration
                                                                    .QueryParameters
                                                                    .Filter = $"appId eq '{appId}'";
                                                            });

            if (!servicePrincipalResponse.Value.Any())
                return false;

            var servicePrincipal = servicePrincipalResponse.Value.First();

            await _graphGroupMetricTracker.TrackMetricsAsync(servicePrincipalResponse.AdditionalData, QueryType.Other, runId);

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                RunId = runId,
                Message = $"Checking if app ID {appId} (service principal with object ID {servicePrincipal.Id}) owns the group {groupObjectId}."
            });

            return await IsGroupOwnerAsync($"id eq '{servicePrincipal.Id}'", groupObjectId, runId);
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
                var groupOwnersResponse = await _graphServiceClient.Groups[groupObjectId.ToString()].Owners.GetAsync(x =>
                {
                    x.QueryParameters.Top = top > 0 ? top : null;
                });

                owners.AddRange(groupOwnersResponse.Value.OfType<User>());
                await _graphGroupMetricTracker.TrackMetricsAsync(groupOwnersResponse.AdditionalData, QueryType.Other, runId);

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = runId,
                    Message = $"Retrieved{(top > 0 ? " top " : " ")}{owners.Count} owners of group {groupObjectId}."
                });

                return owners.Select(x => new AzureADUser
                {
                    ObjectId = Guid.Parse(x.Id),
                    Mail = x.Mail
                }).ToList();
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

        public async Task<bool> IsEmailRecipientOwnerOfGroupAsync(string email, Guid groupObjectId, Guid? runId)
        {

            User user = null;

            try
            {
                user = await _graphServiceClient.Users[email].GetAsync();
            }
            catch (ServiceException ex)
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

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                RunId = runId,
                Message = $"Checking if email owns the group {groupObjectId}."
            });

            return await IsGroupOwnerAsync($"id eq '{user.Id}'", groupObjectId, runId);
        }

        private async Task<bool> IsGroupOwnerAsync(string query, Guid groupObjectId, Guid? runId)
        {
            try
            {
                var groupOwnersResponse = await _graphServiceClient.Groups[groupObjectId.ToString()]
                                                           .Owners.GetAsync(requestConfiguration =>
                                                           {
                                                               requestConfiguration
                                                                .QueryParameters
                                                                .Filter = query;
                                                           });

                await _graphGroupMetricTracker.TrackMetricsAsync(groupOwnersResponse.AdditionalData, QueryType.Other, runId);
                return groupOwnersResponse.Value.Any();
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
