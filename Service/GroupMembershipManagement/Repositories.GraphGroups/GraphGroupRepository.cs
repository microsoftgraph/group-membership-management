// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure;
using Azure.Core;
using Entities;
using Microsoft.ApplicationInsights;
using Microsoft.Graph;
using Newtonsoft.Json.Linq;
using Polly;
using Polly.Retry;
using Repositories.Contracts;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Group = Microsoft.Graph.Group;
using Metric = Services.Entities.Metric;

namespace Repositories.GraphGroups
{
    public class GraphGroupRepository : IGraphGroupRepository
    {
        private readonly IGraphServiceClient _graphServiceClient;
        private readonly TelemetryClient _telemetryClient;
        private readonly ILoggingRepository _loggingRepository;

        public Guid RunId { get; set; }        

        public GraphGroupRepository(IGraphServiceClient graphServiceClient, TelemetryClient telemetryClient, ILoggingRepository logger)
        {
            _graphServiceClient = graphServiceClient;
            _telemetryClient = telemetryClient;
            _loggingRepository = logger;
        }

        private const int MaxRetries = 10;
        private const int MaxResultCount = 999;

        private const string ResourceUnitHeader = "x-ms-resource-unit";
        private const string ThrottlePercentageHeader = "x-ms-throttle-limit-percentage";
        private const string ThrottleInfoHeader = "x-ms-throttle-information";
        private const string ThrottleScopeHeader = "x-ms-throttle-scope";

        public async Task<bool> GroupExists(Guid objectId)
        {
            try
            {
                var group = await _graphServiceClient.Groups[objectId.ToString()].Request().WithMaxRetry(MaxRetries).GetAsync();
                await TrackMetrics(group.AdditionalData, QueryType.Other);
                return group != null;
            }
            catch (ServiceException ex)
            {
                if (ex.StatusCode == HttpStatusCode.NotFound)
                    return false;

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = ex.GetBaseException().ToString(),
                    RunId = RunId
                });

                throw;
            }
        }
        public async Task<bool> GroupExists(string groupName)
        {
            try
            {
                var groupCollectionPage = await _graphServiceClient.Groups.Request().Filter($"startswith(displayName, '{groupName}')").GetAsync();
                return groupCollectionPage.Count > 0;
            }
            catch (ServiceException ex)
            {
                if (ex.StatusCode == HttpStatusCode.NotFound)
                    return false;

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = ex.GetBaseException().ToString(),
                    RunId = RunId
                });

                throw;
            }
        }

        public async Task<AzureADGroup> GetGroup(string groupName)
        {
            try
            {
                var groupCollectionPage = await _graphServiceClient.Groups.Request().Filter($"startswith(displayName, '{groupName}')").GetAsync();
                return new AzureADGroup { ObjectId = new Guid(groupCollectionPage.CurrentPage[0].Id) };
            }
            catch (ServiceException ex)
            {
                if (ex.StatusCode == HttpStatusCode.NotFound)
                    return null;

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = ex.GetBaseException().ToString(),
                    RunId = RunId
                });

                return null;
            }
        }

        public async Task<List<string>> GetGroupEndpointsAsync(Guid groupId)
        {
            var endpoints = new List<string>();
            var baseUrl = "https://graph.microsoft.com";

            try
            {
                var batchRequest = new BatchRequestContent();
                var outlookRequest = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/v1.0/groups/{groupId}?$select=mailEnabled,groupTypes");
                var outlookStep = new BatchRequestStep("outlook", outlookRequest);
                var sharepointRequest = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/beta/groups/{groupId}/sites/root");
                var sharepointStep = new BatchRequestStep("sharepoint", sharepointRequest);

                batchRequest.AddBatchRequestStep(outlookStep);
                batchRequest.AddBatchRequestStep(sharepointStep);

                var batchResponse = await _graphServiceClient.Batch.Request().PostAsync(batchRequest);
                var individualResponses = await batchResponse.GetResponsesAsync();

                if (individualResponses.ContainsKey("outlook") && individualResponses["outlook"].IsSuccessStatusCode)
                {
                    var content = await individualResponses["outlook"].Content.ReadAsStringAsync();
                    var jObject = JObject.Parse(content);
                    var isMailEnabled = jObject.Value<bool>("mailEnabled");
                    var groupTypes = jObject.Value<JArray>("groupTypes").Values<string>().ToList();

                    if (isMailEnabled && groupTypes.Contains("Unified"))
                        endpoints.Add("Outlook");
                }

                if (individualResponses.ContainsKey("sharepoint") && individualResponses["sharepoint"].IsSuccessStatusCode)
                {
                    endpoints.Add("SharePoint");
                }
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = ex.GetBaseException().ToString(),
                    RunId = RunId
                });
            }

            try
            {

                var endpointsUrl = $"{baseUrl}/beta/groups/{groupId}/endpoints";
                var httpRequest = new HttpRequestMessage(HttpMethod.Get, endpointsUrl);
                await _graphServiceClient.AuthenticationProvider.AuthenticateRequestAsync(httpRequest);
                var httpResponse = await _graphServiceClient.HttpProvider.SendAsync(httpRequest);

                if (httpResponse.Content == null)
                    return endpoints;

                var content = await httpResponse.Content.ReadAsStringAsync();

                if (string.IsNullOrWhiteSpace(content))
                    return endpoints;

                endpoints.AddRange(JObject.Parse(content).Value<JArray>("value").Values<string>("providerName").ToList());

            }
            catch (ServiceException ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = ex.GetBaseException().ToString(),
                    RunId = RunId
                });
            }

            return endpoints;
        }

        public async Task CreateGroup(string newGroupName)
        {
            try
            {
                if (await GroupExists(newGroupName))
                {
                    return;
                }

                var group = await _graphServiceClient.Groups.Request().AddAsync(new Group
                {
                    DisplayName = newGroupName,
                    Description = $"Integration test group: {newGroupName}",
                    MailNickname = new Guid().ToString(),
                    MailEnabled = false,
                    SecurityEnabled = true
                });
            }
            catch (Exception e)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Error creating group: {e}" });
            }
        }

        public async Task<List<AzureADUser>> GetTenantUsers(int userCount)
        {
            var tenantUsers = new HashSet<AzureADUser>();

            var userResponse = await _graphServiceClient.Users.Request().GetAsync();
            tenantUsers.UnionWith(userResponse.CurrentPage.Select(graphUser => new AzureADUser { ObjectId = new Guid(graphUser.Id) }));

            while (tenantUsers.Count < userCount && userResponse.NextPageRequest != null)
            {
                userResponse = await userResponse.NextPageRequest.GetAsync();
                tenantUsers.UnionWith(userResponse.CurrentPage.Select(graphUser => new AzureADUser { ObjectId = new Guid(graphUser.Id) }));
            }

            return tenantUsers.ToList();
        }

        public async Task<bool> IsAppIDOwnerOfGroup(string appId, Guid groupObjectId)
        {
            if (await GroupExists(groupObjectId) == false) { return false; }

            // get the service principal ID by its app ID
            var servicePrincipal = (await _graphServiceClient.ServicePrincipals.Request().WithMaxRetry(MaxRetries).Filter($"appId eq '{appId}'").GetAsync()).Single();
            await TrackMetrics(servicePrincipal.AdditionalData, QueryType.Other);

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                RunId = RunId,
                Message = $"Checking if app ID {appId} (service principal with object ID {servicePrincipal.Id}) owns the group {groupObjectId}."
            });

            return await IsGroupOwnerAsync($"id eq '{servicePrincipal.Id}'", groupObjectId);
        }

        public async Task<bool> IsEmailRecipientOwnerOfGroupAsync(string email, Guid groupObjectId)
        {
            if (await GroupExists(groupObjectId) == false) { return false; }

            User user = null;

            try
            {
                user = await _graphServiceClient.Users[email].Request().GetAsync();
            }
            catch (ServiceException ex)
            {
                if (ex.StatusCode == HttpStatusCode.NotFound)
                    return false;

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = ex.GetBaseException().ToString(),
                    RunId = RunId
                });

                throw;
            }

            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                RunId = RunId,
                Message = $"Checking if email owns the group {groupObjectId}."
            });

            return await IsGroupOwnerAsync($"id eq '{user.Id}'", groupObjectId);
        }

        public async Task<List<User>> GetGroupOwnersAsync(Guid groupObjectId, int top = 0)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage
            {
                RunId = RunId,
                Message = $"Getting owners of group {groupObjectId}."
            });

            var owners = new List<User>();

            try
            {
                var request = _graphServiceClient.Groups[groupObjectId.ToString()].Owners
                                        .Request().WithMaxRetry(MaxRetries);

                if (top > 0) request = request.Top(top);

                var groupOwners = await request.GetAsync();
                owners.AddRange(groupOwners.CurrentPage.OfType<User>());
                await TrackMetrics(groupOwners.AdditionalData, QueryType.Other);

                while (groupOwners.NextPageRequest != null)
                {
                    groupOwners = await groupOwners.NextPageRequest.WithMaxRetry(MaxRetries).GetAsync();
                    await TrackMetrics(groupOwners.AdditionalData, QueryType.Other);
                    owners.AddRange(groupOwners.CurrentPage.OfType<User>());
                }

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = RunId,
                    Message = $"Retrieved{(top > 0 ? " top " : " ")}{owners.Count} owners of group {groupObjectId}."
                });

                return owners;
            }
            catch (ServiceException ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = ex.GetBaseException().ToString(),
                    RunId = RunId
                });

                throw;
            }
        }

        public async Task<string> GetGroupNameAsync(Guid objectId)
        {
            try
            {
                var group = await _graphServiceClient.Groups[objectId.ToString()].Request().WithMaxRetry(MaxRetries).GetAsync();
                await TrackMetrics(group.AdditionalData, QueryType.Other);
                return group != null ? group.DisplayName : string.Empty;
            }
            catch (ServiceException ex)
            {
                if (ex.StatusCode == HttpStatusCode.NotFound)
                    return string.Empty;

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = ex.GetBaseException().ToString(),
                    RunId = RunId
                });

                throw;
            }
        }

        public async Task<List<AzureADUser>> GetUsersInGroupTransitively(Guid objectId)
        {
            var nonUserGraphObjects = new Dictionary<string, int>();
            IGroupTransitiveMembersCollectionWithReferencesPage members;

            try
            {
                members = await _graphServiceClient.Groups[objectId.ToString()].TransitiveMembers.Request()
                            .WithMaxRetry(MaxRetries)
                            .Select("id")
                            .GetAsync();
                await TrackMetrics(members.AdditionalData, QueryType.Other);

                var toReturn = new List<AzureADUser>(ToUsers(members.CurrentPage, nonUserGraphObjects));
                while (members.NextPageRequest != null)
                {
                    members = await members.NextPageRequest.WithMaxRetry(MaxRetries).GetAsync();
                    await TrackMetrics(members.AdditionalData, QueryType.Other);
                    toReturn.AddRange(ToUsers(members.CurrentPage, nonUserGraphObjects));
                }

                var nonUserGraphObjectsSummary = string.Join(Environment.NewLine, nonUserGraphObjects.Select(x => $"{x.Value}: {x.Key}"));
                await _loggingRepository.LogMessageAsync(new LogMessage { RunId = RunId, Message = $"From group {objectId}, read {toReturn.Count} users, and the following other directory objects:\n{nonUserGraphObjectsSummary}\n" });
                return toReturn;
            }
            catch (ServiceException ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = ex.GetBaseException().ToString(),
                    RunId = RunId
                });

                throw;
            }
        }

        /// <summary>
        /// get group members page by id.
        /// </summary>
        /// <param name="groupId">group id.</param>
        /// <returns>group members page.</returns>
        public async Task<IGroupTransitiveMembersCollectionWithReferencesPage> GetGroupMembersPageByIdAsync(string groupId)
        {
            var retryPolicy = GetRetryPolicy();
            return await retryPolicy.ExecuteAsync(async () =>
            {
                return await _graphServiceClient
                                .Groups[groupId]
                                .TransitiveMembers
                                .Request()
                                .Top(MaxResultCount)
                                .GetAsync();
            });
        }

        public async Task<IGraphServicePlacesCollectionPage> GetRoomsAsync(string url, int top, int skip)
        {
            var queryParamValues = HttpUtility.ParseQueryString(url, Encoding.UTF8);
            var filterValue = queryParamValues["$filter"];
            var selectValue = queryParamValues["$select"];

            var queryOptions = new List<QueryOption>() { new QueryOption("$count", "true") };
            var roomUrl = _graphServiceClient.Places.AppendSegmentToRequestUrl("microsoft.graph.room");
            var request = new GraphServicePlacesCollectionRequest(roomUrl, _graphServiceClient, queryOptions);
            if (!string.IsNullOrEmpty(filterValue)) { request = (GraphServicePlacesCollectionRequest)request.Filter(filterValue); }
            if (!string.IsNullOrEmpty(selectValue)) { request = (GraphServicePlacesCollectionRequest)request.Select(selectValue); }

            return await request
                            .Top(top)
                            .Skip(skip)
                            .GetAsync();
        }

        public async Task<IGraphServicePlacesCollectionPage> GetWorkSpacesAsync(string url, int top, int skip)
        {
            var queryParamValues = HttpUtility.ParseQueryString(url, Encoding.UTF8);
            var filterValue = queryParamValues["$filter"];
            var selectValue = queryParamValues["$select"];

            var queryOptions = new List<QueryOption>() { new QueryOption("$count", "true") };
            var roomUrl = _graphServiceClient.Places.AppendSegmentToRequestUrl("microsoft.graph.workspace");
            var request = new GraphServicePlacesCollectionRequest(roomUrl, _graphServiceClient, queryOptions);
            if (!string.IsNullOrEmpty(filterValue)) { request = (GraphServicePlacesCollectionRequest)request.Filter(filterValue); }
            if (!string.IsNullOrEmpty(selectValue)) { request = (GraphServicePlacesCollectionRequest)request.Select(selectValue); }

            return await request
                            .Top(top)
                            .Skip(skip)
                            .GetAsync();
        }

        public async Task<IGraphServiceUsersCollectionPage> GetFirstMembersAsync(string url)
        {
            var queryParamValues = HttpUtility.ParseQueryString(url, Encoding.UTF8);
            var searchValue = queryParamValues["$search"];
            var filterValue = queryParamValues["$filter"];
            var orderValue = queryParamValues["$orderBy"];
            var selectValue = queryParamValues["$select"];

            var queryOptions = new List<QueryOption>()
            {
                new QueryOption("$count", "true")
            };

            // if search parameter not missing in url
            if (!string.IsNullOrEmpty(searchValue))
            {
                queryOptions.Add(new QueryOption("$search", searchValue));
            }

            // create base request
            var request = _graphServiceClient
                                    .Users
                                    .Request(queryOptions)
                                    .Header("ConsistencyLevel", "eventual");

            // if filter parameter not missing in url
            if (!string.IsNullOrEmpty(filterValue))
            {
                request = request.Filter(filterValue);
            }

            // if orderby parameter not missing in url
            if (!string.IsNullOrEmpty(orderValue))
            {
                request = request.OrderBy(orderValue);
            }

            // if select parameter not missing in url
            if (!string.IsNullOrEmpty(selectValue))
            {
                request = request.Select(selectValue);
            }

            return await request
                         .Top(MaxResultCount)
                         .GetAsync();
        }

        public async Task<IGraphServiceUsersCollectionPage> GetNextMembersAsync(
            IGraphServiceUsersCollectionPage groupMembersRef,
            string nextPageUrl)
        {
            var retryPolicy = GetRetryPolicy();
            return await retryPolicy.ExecuteAsync(async () =>
            {
                groupMembersRef.InitializeNextPageRequest(_graphServiceClient, nextPageUrl);
                return await groupMembersRef
                                .NextPageRequest
                                .GetAsync();
            });
        }

        /// <summary>
        /// get group members page by next page url.
        /// </summary>
        /// <param name="groupMembersRef">group members page reference.</param>
        /// <param name="nextPageUrl">group members next page data link url.</param>
        /// <returns>group members page.</returns>
        public async Task<IGroupTransitiveMembersCollectionWithReferencesPage> GetGroupMembersNextPageAsnyc(
            IGroupTransitiveMembersCollectionWithReferencesPage groupMembersRef,
            string nextPageUrl)
        {
            var retryPolicy = GetRetryPolicy();
            return await retryPolicy.ExecuteAsync(async () =>
            {
                groupMembersRef.InitializeNextPageRequest(_graphServiceClient, nextPageUrl);
                return await groupMembersRef
                                .NextPageRequest
                                .GetAsync();
            });
        }

        public async Task<(List<AzureADUser> users,
                           IGraphServicePlacesCollectionPage usersFromGroup)> GetRoomsPageAsync(string url, int top, int skip)
        {
            var users = new List<AzureADUser>();
            var response = await GetRoomsAsync(url, top, skip);
            if (response.CurrentPage.Count > 0 && response.CurrentPage[0].AdditionalData != null)
            {
                foreach (var room in response.CurrentPage)
                {
                    var emailAddress = (string)room.AdditionalData["emailAddress"];
                    var user = await GetUserByEmail(emailAddress);
                    if (user != null) users.Add(new AzureADUser { ObjectId = Guid.Parse((string)user.Id) });
                }
            }

            var total = response.AdditionalData.TryGetValue("@odata.count", out object count) ? (int)(long)count : 0;

            if (total > 0)
            {
                var numberOfRequests = total / 100;
                for (int i = 0; i <= numberOfRequests; i++)
                {
                    skip += 100;
                    response = await GetRoomsAsync(url, top, skip);
                    if (response.CurrentPage.Count > 0 && response.CurrentPage[0].AdditionalData != null)
                    {
                        foreach (var room in response.CurrentPage)
                        {
                            var emailAddress = (string)room.AdditionalData["emailAddress"];
                            var user = await GetUserByEmail(emailAddress);
                            if (user != null) users.Add(new AzureADUser { ObjectId = Guid.Parse((string)user.Id) });
                        }
                    }
                }
            }
            return (users, response);
        }

        public async Task<User> GetUserByEmail(string emailAddress)
        {
            User userDetails = null;

            try
            {
                var user = await _graphServiceClient.Users[emailAddress].Request().Select(u => u.Id).GetAsync();
                if (user != null) userDetails = user;
            }

            catch (Exception exception)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    RunId = RunId,
                    Message = $"Exception: {exception}, FailedMethod: {nameof(GetUserByEmail)}, UserEmail: {emailAddress}"
                });
            }

            return userDetails;
        }

        public async Task<(List<AzureADUser> users,
                           IGraphServicePlacesCollectionPage usersFromGroup)> GetWorkSpacesPageAsync(string url, int top, int skip)
        {
            var users = new List<AzureADUser>();
            var response = await GetWorkSpacesAsync(url, top, skip);
            if (response.CurrentPage.Count > 0 && response.CurrentPage[0].AdditionalData != null)
            {
                foreach (var room in response.CurrentPage)
                {
                    var emailAddress = (string)room.AdditionalData["emailAddress"];
                    var user = await GetUserByEmail(emailAddress);
                    if (user != null) users.Add(new AzureADUser { ObjectId = Guid.Parse((string)user.Id) });
                }
            }


            var total = response.AdditionalData.TryGetValue("@odata.count", out object count) ? (int)(long)count : 0;

            if (total > 0)
            {
                var numberOfRequests = total / 100;
                for (int i = 0; i <= numberOfRequests; i++)
                {
                    skip += 100;
                    response = await GetWorkSpacesAsync(url, top, skip);
                    if (response.CurrentPage.Count > 0 && response.CurrentPage[0].AdditionalData != null)
                    {
                        foreach (var room in response.CurrentPage)
                        {
                            var emailAddress = (string)room.AdditionalData["emailAddress"];
                            var user = await GetUserByEmail(emailAddress);
                            if (user != null) users.Add(new AzureADUser { ObjectId = Guid.Parse((string)user.Id) });
                        }
                    }
                }
            }
            return (users, response);
        }

        public async Task<(List<AzureADUser> users,
                           Dictionary<string, int> nonUserGraphObjects,
                           string nextPageUrl,
                           IGraphServiceUsersCollectionPage usersFromGroup)> GetFirstMembersPageAsync(string url)
        {

            var users = new List<AzureADUser>();
            var nonUserGraphObjects = new Dictionary<string, int>();
            var response = await GetFirstMembersAsync(url);
            TrackMetrics(response.AdditionalData);
            var nextPageUrl = response.AdditionalData.TryGetValue("@odata.nextLink", out object nextLink1) ? nextLink1.ToString() : string.Empty;
            users.AddRange(ToUsers(response, nonUserGraphObjects));
            return (users, nonUserGraphObjects, nextPageUrl, response);
        }

        public async Task<(List<AzureADUser> users,
                           Dictionary<string, int> nonUserGraphObjects,
                           string nextPageUrl,
                           IGraphServiceUsersCollectionPage usersFromGroup)> GetNextMembersPageAsync(string nextPageUrl, IGraphServiceUsersCollectionPage usersFromGroup)
        {
            var users = new List<AzureADUser>();
            var nonUserGraphObjects = new Dictionary<string, int>();
            usersFromGroup = await GetNextMembersAsync(usersFromGroup, nextPageUrl);
            TrackMetrics(usersFromGroup.AdditionalData);
            nextPageUrl = usersFromGroup.AdditionalData.TryGetValue("@odata.nextLink", out object nextLink2) ? nextLink2.ToString() : string.Empty;
            users.AddRange(ToUsers(usersFromGroup, nonUserGraphObjects));
            return (users, nonUserGraphObjects, nextPageUrl, usersFromGroup);
        }

        public async Task<(List<AzureADUser> users,
                           Dictionary<string, int> nonUserGraphObjects,
                           string nextPageUrl,
                           IGroupTransitiveMembersCollectionWithReferencesPage usersFromGroup)> GetFirstTransitiveMembersPageAsync(Guid objectId)
        {
            var users = new List<AzureADUser>();
            var nonUserGraphObjects = new Dictionary<string, int>();

            var usersFromGroup = await GetGroupMembersPageByIdAsync(objectId.ToString());
            await TrackMetrics(usersFromGroup.AdditionalData, QueryType.Transitive);
            usersFromGroup.AdditionalData.TryGetValue("@odata.nextLink", out object nextLink1);
            var nextPageUrl = (nextLink1 == null) ? string.Empty : nextLink1.ToString();
            users.AddRange(ToUsers(usersFromGroup, nonUserGraphObjects));
            return (users, nonUserGraphObjects, nextPageUrl, usersFromGroup);
        }

        public async Task<(List<AzureADUser> users,
                           Dictionary<string, int> nonUserGraphObjects,
                           string nextPageUrl,
                           IGroupTransitiveMembersCollectionWithReferencesPage usersFromGroup)> GetNextTransitiveMembersPageAsync(string nextPageUrl, IGroupTransitiveMembersCollectionWithReferencesPage usersFromGroup)
        {
            var users = new List<AzureADUser>();
            var nonUserGraphObjects = new Dictionary<string, int>();

            usersFromGroup = await GetGroupMembersNextPageAsnyc(usersFromGroup, nextPageUrl);
            await TrackMetrics(usersFromGroup.AdditionalData, QueryType.Transitive);
            usersFromGroup.AdditionalData.TryGetValue("@odata.nextLink", out object nextLink2);
            nextPageUrl = (nextLink2 == null) ? string.Empty : nextLink2.ToString();
            users.AddRange(ToUsers(usersFromGroup, nonUserGraphObjects));
            return (users, nonUserGraphObjects, nextPageUrl, usersFromGroup);
        }

        public async Task<IGroupDeltaCollectionPage> GetGroupUsersPageByLinkAsync(string deltaLink)
        {
            var groupCollectionPage = new GroupDeltaCollectionPage();
            groupCollectionPage.InitializeNextPageRequest(_graphServiceClient, deltaLink);
            return await groupCollectionPage.NextPageRequest.GetAsync();
        }

        public async Task<IGroupDeltaCollectionPage> GetGroupUsersPageByIdAsync(string groupId)
        {
            var retryPolicy = GetRetryPolicy();
            return await retryPolicy.ExecuteAsync(async () =>
            {
                return await _graphServiceClient
                                    .Groups
                                    .Delta()
                                    .Request()
                                    .Filter($"id  eq '{groupId}'")
                                    .Top(MaxResultCount)
                                    .GetAsync();
            });
        }
        public async Task<IGroupDeltaCollectionPage> GetGroupUsersNextPageAsnyc(IGroupDeltaCollectionPage groupMembersRef, string nextPageUrl)
        {
            var retryPolicy = GetRetryPolicy();
            return await retryPolicy.ExecuteAsync(async () =>
            {
                groupMembersRef.InitializeNextPageRequest(_graphServiceClient, nextPageUrl);
                return await groupMembersRef
                                .NextPageRequest
                                .GetAsync();
            });
        }
        public async Task<int> GetGroupsCountAsync(Guid objectId)
        {
            var resourceUnitsUsed = _telemetryClient.GetMetric(nameof(Metric.ResourceUnitsUsed));
            var throttleLimitPercentage = _telemetryClient.GetMetric(nameof(Metric.ThrottleLimitPercentage));

            var requestUrl = _graphServiceClient.Groups[objectId.ToString()].TransitiveMembers.Request().RequestUrl;

            // add casting and count query
            requestUrl = $"{requestUrl}/microsoft.graph.group/$count";

            // Create the request message
            var hrm = new HttpRequestMessage(HttpMethod.Get, requestUrl);

            // $count requires header ConsistencyLevel
            hrm.Headers.Add("ConsistencyLevel", "eventual");

            // Authenticate (add access token) our HttpRequestMessage
            await _graphServiceClient.AuthenticationProvider.AuthenticateRequestAsync(hrm);

            // Send the request and get the response.
            var r = await _graphServiceClient.HttpProvider.SendAsync(hrm);

            if (r.Headers.TryGetValues(ResourceUnitHeader, out var resourceValues))
                resourceUnitsUsed.TrackValue(ParseFirst<int>(resourceValues, int.TryParse));

            if (r.Headers.TryGetValues(ThrottlePercentageHeader, out var throttleValues))
                throttleLimitPercentage.TrackValue(ParseFirst<double>(throttleValues, double.TryParse));

            // read the content and parse it as an integer
            var content = await r.Content.ReadAsStringAsync();
            var groupCount = int.Parse(content);

            return groupCount;
        }

        public async Task<(List<AzureADUser> users, string nextPageUrl, string deltaUrl, IGroupDeltaCollectionPage usersFromGroup)> GetFirstUsersPageAsync(Guid objectId)
        {            
            var users = new List<AzureADUser>();
            var response = await GetGroupUsersPageByIdAsync(objectId.ToString());
            await TrackMetrics(response.AdditionalData, QueryType.Delta);
            response.AdditionalData.TryGetValue("@odata.nextLink", out object nextLink1);
            var nextPageUrl = (nextLink1 == null) ? string.Empty : nextLink1.ToString();
            response.AdditionalData.TryGetValue("@odata.deltaLink", out object deltaLink1);
            var deltaUrl = (deltaLink1 == null) ? string.Empty : deltaLink1.ToString();

            if (response.CurrentPage.Count > 0 && response.CurrentPage[0].AdditionalData != null)
            {
                if (!response.CurrentPage[0].AdditionalData.TryGetValue("members@delta", out object members))
                {
                    return (users, nextPageUrl, deltaUrl, response);
                }
                foreach (JObject user in (JArray)members)
                {
                    if (user["@odata.type"].ToString().Equals("#microsoft.graph.user", StringComparison.InvariantCultureIgnoreCase) &&
                        user["@removed"] == null)
                    {
                        users.Add(new AzureADUser { ObjectId = Guid.Parse((string)user["id"]) });
                    }
                }
            }

            return (users, nextPageUrl, deltaUrl, response);
        }

        public async Task<(List<AzureADUser> users, string nextPageUrl, string deltaUrl, IGroupDeltaCollectionPage usersFromGroup)> GetNextUsersPageAsync(string nextPageUrl, IGroupDeltaCollectionPage response)
        {            
            var users = new List<AzureADUser>();
            response = await GetGroupUsersNextPageAsnyc(response, nextPageUrl);
            await TrackMetrics(response.AdditionalData, QueryType.Delta);
            response.AdditionalData.TryGetValue("@odata.nextLink", out object nextLink1);
            nextPageUrl = (nextLink1 == null) ? string.Empty : nextLink1.ToString();
            response.AdditionalData.TryGetValue("@odata.deltaLink", out object deltaLink1);
            var deltaUrl = (deltaLink1 == null) ? string.Empty : deltaLink1.ToString();

            if (response.CurrentPage.Count > 0 && response.CurrentPage[0].AdditionalData != null)
            {
                if (!response.CurrentPage[0].AdditionalData.TryGetValue("members@delta", out object members))
                {
                    return (users, nextPageUrl, deltaUrl, response);
                }

                foreach (JObject user in (JArray)members)
                {
                    if (user["@odata.type"].ToString().Equals("#microsoft.graph.user", StringComparison.InvariantCultureIgnoreCase) &&
                        user["@removed"] == null)
                    {
                        users.Add(new AzureADUser { ObjectId = Guid.Parse((string)user["id"]) });
                    }
                }
            }

            return (users, nextPageUrl, deltaUrl, response);
        }

        public async Task<(List<AzureADUser> usersToAdd, List<AzureADUser> usersToRemove, string nextPageUrl, string deltaUrl, IGroupDeltaCollectionPage usersFromGroup)> GetFirstDeltaUsersPageAsync(string deltaLink)
        {           
            var usersToAdd = new List<AzureADUser>();
            var usersToRemove = new List<AzureADUser>();
            var response = await GetGroupUsersPageByLinkAsync(deltaLink);
            await TrackMetrics(response.AdditionalData, QueryType.DeltaLink);
            response.AdditionalData.TryGetValue("@odata.nextLink", out object nextLink1);
            var nextPageUrl = (nextLink1 == null) ? string.Empty : nextLink1.ToString();
            response.AdditionalData.TryGetValue("@odata.deltaLink", out object deltaLink1);
            var deltaUrl = (deltaLink1 == null) ? string.Empty : deltaLink1.ToString();

            if (response.CurrentPage.Count > 0 && response.CurrentPage[0].AdditionalData != null)
            {
                if (!response.CurrentPage[0].AdditionalData.TryGetValue("members@delta", out object members))
                {
                    return (usersToAdd, usersToRemove, nextPageUrl, deltaUrl, response);
                }

                foreach (JObject user in (JArray)members)
                {
                    if (user["@odata.type"].ToString().Equals("#microsoft.graph.user", StringComparison.InvariantCultureIgnoreCase))
                    {
                        if (user["@removed"] == null)
                        {
                            usersToAdd.Add(new AzureADUser { ObjectId = Guid.Parse((string)user["id"]) });
                        }
                        else
                        {
                            usersToRemove.Add(new AzureADUser { ObjectId = Guid.Parse((string)user["id"]) });
                        }
                    }
                }
            }

            return (usersToAdd, usersToRemove, nextPageUrl, deltaUrl, response);
        }

        public async Task<(List<AzureADUser> usersToAdd, List<AzureADUser> usersToRemove, string nextPageUrl, string deltaUrl, IGroupDeltaCollectionPage usersFromGroup)> GetNextDeltaUsersPageAsync(string nextPageUrl, IGroupDeltaCollectionPage response)
        {           
            var usersToAdd = new List<AzureADUser>();
            var usersToRemove = new List<AzureADUser>();
            response = await GetGroupUsersNextPageAsnyc(response, nextPageUrl);
            await TrackMetrics(response.AdditionalData, QueryType.DeltaLink);
            response.AdditionalData.TryGetValue("@odata.nextLink", out object nextLink1);
            nextPageUrl = (nextLink1 == null) ? string.Empty : nextLink1.ToString();
            response.AdditionalData.TryGetValue("@odata.deltaLink", out object deltaLink1);
            var deltaUrl = (deltaLink1 == null) ? string.Empty : deltaLink1.ToString();

            if (response.CurrentPage.Count > 0 && response.CurrentPage[0].AdditionalData != null)
            {
                if (!response.CurrentPage[0].AdditionalData.TryGetValue("members@delta", out object members))
                {
                    return (usersToAdd, usersToRemove, nextPageUrl, deltaUrl, response);
                }
                foreach (JObject user in (JArray)members)
                {
                    if (user["@odata.type"].ToString().Equals("#microsoft.graph.user", StringComparison.InvariantCultureIgnoreCase))
                    {
                        if (user["@removed"] == null)
                        {
                            usersToAdd.Add(new AzureADUser { ObjectId = Guid.Parse((string)user["id"]) });
                        }
                        else
                        {
                            usersToRemove.Add(new AzureADUser { ObjectId = Guid.Parse((string)user["id"]) });
                        }
                    }
                }
            }

            return (usersToAdd, usersToRemove, nextPageUrl, deltaUrl, response);
        }

        public async Task<IEnumerable<IAzureADObject>> GetChildrenOfGroup(Guid objectId)
        {
            IGroupMembersCollectionWithReferencesPage members;

            try
            {
                members = await _graphServiceClient.Groups[objectId.ToString()].Members.Request()
                .WithMaxRetry(MaxRetries)
                .Select("id")
                .GetAsync();

                await TrackMetrics(members.AdditionalData, QueryType.Other);

                var toReturn = new List<IAzureADObject>(ToEntities(members.CurrentPage));
                while (members.NextPageRequest != null)
                {
                    members = await members.NextPageRequest.WithMaxRetry(MaxRetries).GetAsync();
                    toReturn.AddRange(ToEntities(members.CurrentPage));
                }
                return toReturn;
            }
            catch (ServiceException ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = "Unable to retrieve group members.\n" + ex.GetBaseException().ToString(),
                    RunId = RunId
                });

                throw;
            }
        }

        public async Task TrackMetrics(IDictionary<string, object> additionalData, QueryType queryType)
        {
            int ruu = 0;
            var ruuCustomEvent = new ResourceUnitCustomEvent();
            ruuCustomEvent.RunId = RunId.ToString();
            ruuCustomEvent.QueryType = Enum.GetName(typeof(QueryType), queryType);

            if (queryType == QueryType.Delta || queryType == QueryType.DeltaLink)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Resource unit cost of delta related queries: {Enum.GetName(typeof(QueryType), queryType)}", RunId = RunId }, VerbosityLevel.DEBUG);
                ruu = 5;
                ruuCustomEvent.ResourceUnit = ruu.ToString();                
                TrackRUUEvent(ruuCustomEvent);
                _telemetryClient.GetMetric(nameof(Metric.ResourceUnitsUsed)).TrackValue(ruu);
                return;
            }

            // some replies just don't have the response headers
            // i suspect those either aren't throttled the same way or it's a different kind of call
            if (!additionalData.TryGetValue("responseHeaders", out var headers))
                return;

            // see https://github.com/microsoftgraph/msgraph-sdk-dotnet/blob/dev/docs/headers.md#reading-response-headers
            var responseHeaders = _graphServiceClient.HttpProvider.Serializer.DeserializeObject<Dictionary<string, List<string>>>(headers.ToString());

            if (responseHeaders.TryGetValue(ResourceUnitHeader, out var resourceValues))
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Resource unit cost of delta unrelated queries: {Enum.GetName(typeof(QueryType), queryType)}", RunId = RunId }, VerbosityLevel.DEBUG);
                ruu = ParseFirst<int>(resourceValues, int.TryParse);
                ruuCustomEvent.ResourceUnit = ruu.ToString();
                TrackRUUEvent(ruuCustomEvent);
                _telemetryClient.GetMetric(nameof(Metric.ResourceUnitsUsed)).TrackValue(ruu);
            }

            if (responseHeaders.TryGetValue(ThrottlePercentageHeader, out var throttleValues))
                _telemetryClient.GetMetric(nameof(Metric.ThrottleLimitPercentage)).TrackValue(ParseFirst<double>(throttleValues, double.TryParse));
        }

        private void TrackRUUEvent(ResourceUnitCustomEvent ruuCustomEvent)
        {        
            var ruuDict = ruuCustomEvent.GetType()
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .ToDictionary(prop => prop.Name, prop => (string)prop.GetValue(ruuCustomEvent, null));

            _telemetryClient.TrackEvent(nameof(Metric.ResourceUnitsUsed), ruuDict);
        }

        public async Task<int> GetUsersCountAsync(Guid objectId)
        {
            var resourceUnitsUsed = _telemetryClient.GetMetric(nameof(Metric.ResourceUnitsUsed));
            var throttleLimitPercentage = _telemetryClient.GetMetric(nameof(Metric.ThrottleLimitPercentage));
            var requestUrl = _graphServiceClient.Groups[objectId.ToString()].TransitiveMembers.Request().RequestUrl;
            requestUrl = $"{requestUrl}/microsoft.graph.user/$count";
            var hrm = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            hrm.Headers.Add("ConsistencyLevel", "eventual");
            await _graphServiceClient.AuthenticationProvider.AuthenticateRequestAsync(hrm);
            var r = await _graphServiceClient.HttpProvider.SendAsync(hrm);
            if (r.Headers.TryGetValues(ResourceUnitHeader, out var resourceValues))
                resourceUnitsUsed.TrackValue(ParseFirst<int>(resourceValues, int.TryParse));
            if (r.Headers.TryGetValues(ThrottlePercentageHeader, out var throttleValues))
                throttleLimitPercentage.TrackValue(ParseFirst<double>(throttleValues, double.TryParse));
            var content = await r.Content.ReadAsStringAsync();
            var userCount = int.Parse(content);
            return userCount;
        }

        const int GraphBatchLimit = 20;
        const int ConcurrentRequests = 10;
        public Task<(ResponseCode ResponseCode, int SuccessCount, List<AzureADUser> UsersNotFound)> AddUsersToGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup)
        {
            //You can, in theory, send batches of 20 requests of 20 group adds each
            // but Graph starts saying "Service Unavailable" for a bunch of them if you do that, so only send so many at once
            // 5 seems to be the most without it starting to throw errors that have to be retried
            return BatchAndSend(users, b => MakeBulkAddRequest(b, targetGroup.ObjectId), GraphBatchLimit, 5);
        }

        public Task<(ResponseCode ResponseCode, int SuccessCount, List<AzureADUser> UsersNotFound)> RemoveUsersFromGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup)
        {
            // This, however, is the most we can send per delete batch, and it works pretty well.
            return BatchAndSend(users, b => MakeBulkRemoveRequest(b, targetGroup.ObjectId), 1, GraphBatchLimit);
        }

        delegate HttpRequestMessage MakeBulkRequest(List<AzureADUser> batch);
        private class ChunkOfUsers
        {
            public List<AzureADUser> ToSend { get; set; }
            public string Id { get; set; }

            private const int MaxBatchRetries = 5;

            // basically, whenever a batch is retried, we append the thread number after a dash
            public bool ShouldRetry => Id.Split('-')[1].Length < MaxBatchRetries;
            public ChunkOfUsers UpdateIdForRetry(int threadNumber)
            {
                Id += threadNumber;
                return this;
            }
        }

        private string GetNewChunkId() => $"{Guid.NewGuid().ToString().Replace("-", string.Empty)}-";

        private async Task<(ResponseCode ResponseCode, int SuccessCount, List<AzureADUser> UsersNotFound)> BatchAndSend(IEnumerable<AzureADUser> users, MakeBulkRequest makeRequest, int requestMax, int batchSize)
        {
            if (!users.Any()) { return (ResponseCode.Ok, 0, new List<AzureADUser>()); }

            var queuedBatches = new ConcurrentQueue<ChunkOfUsers>(
                    ChunksOfSize(users, requestMax) // Chop up the users into chunks of how many per graph request (20 for add, 1 for remove)
                    .Select(x => new ChunkOfUsers
                    {
                        ToSend = x,
                        Id = x[0].MembershipAction == MembershipAction.Add ? GetNewChunkId() : x[0].ObjectId.ToString()
                    }));

            var responses = await Task.WhenAll(Enumerable.Range(0, ConcurrentRequests).Select(x => ProcessQueue(queuedBatches, makeRequest, x, batchSize)));

            var status = responses.Any(x => x.ResponseCode == ResponseCode.Error) ? ResponseCode.Error : ResponseCode.Ok;
            return (status, responses.Sum(x => x.SuccessCount), usersNotFound);
        }

        private async Task<(ResponseCode ResponseCode, int SuccessCount)> ProcessQueue(ConcurrentQueue<ChunkOfUsers> queue, MakeBulkRequest makeRequest, int threadNumber, int batchSize)
        {
            var successCount = 0;

            do
            {
                var toSend = new List<ChunkOfUsers>();
                while (queue.TryDequeue(out var step))
                {
                    toSend.Add(step);
                    if (toSend.Count == batchSize)
                    {
                        var response = await ProcessBatch(queue, toSend, makeRequest, threadNumber);
                        toSend.Clear();

                        successCount += response.SuccessCount;

                        if (response.ResponseCode == ResponseCode.Error)
                            return response;
                    }
                }

                if (toSend.Any())
                {
                    var response = await ProcessBatch(queue, toSend, makeRequest, threadNumber);

                    successCount += response.SuccessCount;

                    if (response.ResponseCode == ResponseCode.Error)
                        return response;
                }

            } while (!queue.IsEmpty); // basically, that last ProcessBatch may have put more stuff in the queue

            return (ResponseCode.Ok, successCount);
        }

        private async Task<(ResponseCode ResponseCode, int SuccessCount)> ProcessBatch(ConcurrentQueue<ChunkOfUsers> queue, List<ChunkOfUsers> toSend, MakeBulkRequest makeRequest, int threadNumber)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Thread number {threadNumber}: Sending a batch of {toSend.Count} requests.", RunId = RunId }, VerbosityLevel.DEBUG);
            int requeued = 0;
            bool hasUnrecoverableErrors = false;
            var successfulRequests = toSend.SelectMany(x => x.ToSend).ToList().Count;

            try
            {
                await foreach (var idToRetry in await SendBatch(new BatchRequestContent(toSend.Select(x => new BatchRequestStep(x.Id, makeRequest(x.ToSend))).ToArray())))
                {
                    var chunkToRetry = toSend.First(x => x.Id == idToRetry.RequestId);

                    successfulRequests -= chunkToRetry.ToSend.Count;

                    if (idToRetry.ResponseCode == ResponseCode.Error)
                    {
                        hasUnrecoverableErrors = true;
                        break;
                    }

                    if (chunkToRetry.ShouldRetry)
                    {
                        // Not found
                        if (!string.IsNullOrWhiteSpace(idToRetry.AzureObjectId))
                        {
                            var notFoundUser = chunkToRetry.ToSend.FirstOrDefault(x => x.ObjectId.ToString().Equals(idToRetry.AzureObjectId, StringComparison.InvariantCultureIgnoreCase));
                            if (notFoundUser != null)
                            {
                                chunkToRetry.ToSend.Remove(notFoundUser);
                            }

                            if (chunkToRetry.ToSend.Count == 1 && chunkToRetry.ToSend[0].MembershipAction == MembershipAction.Remove)
                            {
                                continue;
                            }
                        }

                        // Break down request for individual retries
                        if (chunkToRetry.ToSend.Count > 1 && idToRetry.ResponseCode == ResponseCode.IndividualRetry)
                        {
                            var chunksOfUsers = chunkToRetry.ToSend.Select(x => new ChunkOfUsers
                            {
                                Id = GetNewChunkId(),
                                ToSend = new List<AzureADUser> { x }
                            });

                            foreach (var chunk in chunksOfUsers)
                            {
                                requeued++;
                                queue.Enqueue(chunk.UpdateIdForRetry(threadNumber));
                                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Queued {chunk.Id} from {chunkToRetry.Id}", RunId = RunId });
                            }

                            chunkToRetry.ToSend.Clear();
                        }

                        if (chunkToRetry.ToSend.Count > 0)
                        {
                            requeued++;
                            var originalId = chunkToRetry.Id;
                            queue.Enqueue(chunkToRetry.UpdateIdForRetry(threadNumber));
                            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Requeued {originalId} as {chunkToRetry.Id}", RunId = RunId });
                        }
                    }
                }
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Thread number {threadNumber}: {toSend.Count - requeued} out of {toSend.Count} requests succeeded. {queue.Count} left.", RunId = RunId }, VerbosityLevel.DEBUG);
            }
            catch (ServiceException ex)
            {
                // winding up in here is a pretty rare event
                // Usually, it's because either a timeout happened or something else weird went on
                // the best thing to do is just requeue the chunks
                // but if a chunk has already been queued five times or so, drop it on the floor so we don't go forever
                // in the future, log the exception and which ones get dropped.

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = ex.GetBaseException().ToString(),
                    RunId = RunId
                });

                foreach (var chunk in toSend)
                {
                    if (chunk.ShouldRetry)
                    {
                        var originalId = chunk.Id;
                        queue.Enqueue(chunk.UpdateIdForRetry(threadNumber));

                        await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Requeued {originalId} as {chunk.Id}", RunId = RunId });
                    }
                }
            }

            var status = hasUnrecoverableErrors ? ResponseCode.Error : ResponseCode.Ok;
            return (status, successfulRequests);
        }

        private async Task<IAsyncEnumerable<RetryResponse>> SendBatch(BatchRequestContent tosend)
        {
            try
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Sending requests {string.Join(",", tosend.BatchRequestSteps.Keys)}.", RunId = RunId });

                var response = await _graphServiceClient.Batch.Request().PostAsync(tosend);
                return GetStepIdsToRetry(await response.GetResponsesAsync(), (Dictionary<string, BatchRequestStep>)tosend.BatchRequestSteps);
            }
            catch (ServiceException ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = ex.GetBaseException().ToString(),
                    RunId = RunId
                });

                throw;
            }
        }

        private static readonly HttpStatusCode[] _shouldRetry = new[] { HttpStatusCode.ServiceUnavailable, HttpStatusCode.GatewayTimeout, HttpStatusCode.BadGateway, HttpStatusCode.InternalServerError };
        private static readonly HttpStatusCode[] _isOkay = new[] { HttpStatusCode.NoContent, HttpStatusCode.NotFound, HttpStatusCode.OK };

        // These indicate that we're trying to remove a user that's already been removed.
        // Probably because an ID from earlier finally went through between the first try and the retry.
        private static readonly string[] _okayErrorMessages =
            {
                "One or more removed object references do not exist for the following modified properties: 'members'.",
                "One or more added object references already exist for the following modified properties: 'members'."
            };

        private static readonly Regex _userNotFound = new Regex(@"Resource '(?<id>[({]?[a-fA-F0-9]{8}[-]?([a-fA-F0-9]{4}[-]?){3}[a-fA-F0-9]{12}[})]?)' does not exist", RegexOptions.IgnoreCase);
        private List<AzureADUser> usersNotFound = new List<AzureADUser>();

        private async IAsyncEnumerable<RetryResponse> GetStepIdsToRetry(Dictionary<string, HttpResponseMessage> responses, Dictionary<string, BatchRequestStep> requests)
        {
            bool beenThrottled = false;

            var resourceUnitsUsed = _telemetryClient.GetMetric(nameof(Metric.ResourceUnitsUsed));
            var throttleLimitPercentage = _telemetryClient.GetMetric(nameof(Metric.ThrottleLimitPercentage));
            var writesUsed = _telemetryClient.GetMetric(nameof(Metric.WritesUsed));

            foreach (var kvp in responses)
            {
                //Ensure that the response messages get disposed of.
                using var response = kvp.Value;
                var status = response.StatusCode;
                var content = await response.Content.ReadAsStringAsync();

                if (response.Headers.TryGetValues(ResourceUnitHeader, out var resourceValues))
                    resourceUnitsUsed.TrackValue(ParseFirst<int>(resourceValues, int.TryParse));

                if (response.Headers.TryGetValues(ThrottlePercentageHeader, out var throttleValues))
                    throttleLimitPercentage.TrackValue(ParseFirst<double>(throttleValues, double.TryParse));

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Response - RequestId:{kvp.Key} - StatusCode:{status} - Content:{content}",
                    RunId = RunId
                }, VerbosityLevel.DEBUG);


                // Note that the ones with empty bodies mean "this response is okay and we don't have to do anything about it."
                if (status == HttpStatusCode.BadRequest && IsOkayError(content)) { }
                else if (status == HttpStatusCode.NotFound && (content).Contains("does not exist or one of its queried reference-property objects are not present."))
                {
                    var match = _userNotFound.Match(content);
                    var userId = default(string);

                    if (match.Success)
                    {
                        userId = match.Groups["id"].Value;
                        await _loggingRepository.LogMessageAsync(new LogMessage
                        {
                            Message = $"User ID is found",
                            RunId = RunId
                        });

                        var requestStep = requests[kvp.Key];

                        if (requestStep.Request.Method == HttpMethod.Delete)
                        {
                            await _loggingRepository.LogMessageAsync(new LogMessage
                            {
                                Message = $"Removing {requestStep.RequestId} failed as this resource does not exit",
                                RunId = RunId
                            });

                            usersNotFound.Add(new AzureADUser { ObjectId = Guid.Parse(requestStep.RequestId) });
                        }
                        else
                        {
                            await _loggingRepository.LogMessageAsync(new LogMessage
                            {
                                Message = $"Adding {userId} failed as this resource does not exit",
                                RunId = RunId
                            });

                            usersNotFound.Add(new AzureADUser { ObjectId = Guid.Parse(userId) });
                        }
                    }

                    else
                    {
                        await _loggingRepository.LogMessageAsync(new LogMessage
                        {
                            Message = $"User ID is missing",
                            RunId = RunId
                        });

                        yield return new RetryResponse
                        {
                            RequestId = kvp.Key,
                            ResponseCode = ResponseCode.IndividualRetry
                        };
                    }

                    yield return new RetryResponse
                    {
                        RequestId = kvp.Key,
                        ResponseCode = ResponseCode.IndividualRetry,
                        AzureObjectId = userId
                    };

                }
                else if (_isOkay.Contains(status)) { writesUsed.TrackValue(1); }
                else if (status == HttpStatusCode.TooManyRequests)
                {
                    // basically, each request in the batch will probably say it's been throttled
                    // but we only need to wait the first time.
                    // this isn't strictly true- i believe that the count gets reset if any other threads send requests
                    // but it's true enough until we can engineer something more robust

                    if (!beenThrottled)
                    {
                        // basically, go ahead and start waiting while we log the throttling info
                        // add a few seconds to account for other 419s that happen before we can send the signal to pause.
                        var throttleWait = CalculateThrottleWait(response.Headers.RetryAfter) + TimeSpan.FromSeconds(10);

                        var startThrottling = Task.Delay(throttleWait);
                        var gotThrottleInfo = response.Headers.TryGetValues(ThrottleInfoHeader, out var throttleInfo);
                        var gotThrottleScope = response.Headers.TryGetValues(ThrottleScopeHeader, out var throttleScope);
                        await _loggingRepository.LogMessageAsync(new LogMessage
                        {
                            Message = string.Format("Got 429 throttled. Waiting {0} seconds. Delta: {1} Date: {2} Reason: {3} Scope: {4}",
                                throttleWait.TotalSeconds,
                                response.Headers.RetryAfter.Delta != null ? response.Headers.RetryAfter.Delta.ToString() : "(none)",
                                response.Headers.RetryAfter.Date != null ? response.Headers.RetryAfter.Date.ToString() : "(none)",
                                gotThrottleInfo ? string.Join(',', throttleInfo) : "(none)",
                                gotThrottleScope ? string.Join(',', throttleScope) : "(none)"),
                            RunId = RunId
                        });
                        await startThrottling;
                        beenThrottled = true;
                    }

                    // it's possible for only some requests in a batch to be throttled, so only retry the ones that were throttled.
                    yield return new RetryResponse
                    {
                        RequestId = kvp.Key,
                        ResponseCode = ResponseCode.IndividualRetry
                    };
                }
                else if (status == HttpStatusCode.Forbidden && content.Contains("Guests users are not allowed to join"))
                {
                    yield return new RetryResponse
                    {
                        RequestId = kvp.Key,
                        ResponseCode = ResponseCode.IndividualRetry
                    };
                }
                else if (_shouldRetry.Contains(status))
                {
                    yield return new RetryResponse
                    {
                        RequestId = kvp.Key,
                        ResponseCode = ResponseCode.Ok
                    };
                }
                else
                {
                    await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Got an unexpected error from Graph, stopping all processing for current job: {status} {response.ReasonPhrase} {content}.", RunId = RunId });
                    yield return new RetryResponse
                    {
                        RequestId = kvp.Key,
                        ResponseCode = ResponseCode.Error
                    };
                }
            }
        }

        delegate bool TryParseFunction<T>(string str, out T parsed);
        private static T ParseFirst<T>(IEnumerable<string> toParse, TryParseFunction<T> tryParse)
        {
            foreach (var str in toParse)
            {
                if (tryParse(str, out var parsed))
                {
                    return parsed;
                }
            }

            return default;
        }

        private static bool IsOkayError(string error)
        {
            error = JObject.Parse(error)["error"]["message"].Value<string>();
            return _okayErrorMessages.Any(x => error.Contains(x));
        }
        private static TimeSpan CalculateThrottleWait(RetryConditionHeaderValue wait)
        {
            // we're much more likely to hit the write quota, so default to the 2 minute and 30 second wait
            // https://docs.microsoft.com/en-us/graph/throttling#pattern
            TimeSpan waitFor = TimeSpan.FromSeconds(150);
            if (wait.Delta.HasValue) { waitFor = wait.Delta.Value; }
            if (wait.Date.HasValue) { waitFor = wait.Date.Value - DateTimeOffset.UtcNow; }
            return waitFor;
        }

        private HttpRequestMessage MakeBulkAddRequest(List<AzureADUser> batch, Guid targetGroup)
        {
            return new HttpRequestMessage(HttpMethod.Patch, $"https://graph.microsoft.com/v1.0/groups/{targetGroup}")
            {
                Content = new StringContent(MakeAddRequestBody(batch), System.Text.Encoding.UTF8, "application/json"),
            };
        }

        private HttpRequestMessage MakeBulkRemoveRequest(List<AzureADUser> batch, Guid targetGroup)
        {
            // You have to remove users with their object ID. UPN won't work because you can only use it when the thing you're removing is
            // unambiguously a user.

            if (batch.Count != 1) { throw new ArgumentException("Batches of deletes must have exactly one item. This one has " + batch.Count); }

            var toRemove = batch.Single().ObjectId;
            return new HttpRequestMessage(HttpMethod.Delete, $"https://graph.microsoft.com/v1.0/groups/{targetGroup}/members/{toRemove}/$ref");
        }

        private static string MakeAddRequestBody(List<AzureADUser> users)
        {
            JObject body = new JObject
            {
                ["members@odata.bind"] = JArray.FromObject(users.Select(x => $"https://graph.microsoft.com/v1.0/users/{x.ObjectId}"))
            };
            return body.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static IEnumerable<List<T>> ChunksOfSize<T>(IEnumerable<T> enumerable, int chunkSize)
        {
            var toReturn = new List<T>();
            foreach (var item in enumerable)
            {
                if (toReturn.Count == chunkSize)
                {
                    yield return toReturn;
                    toReturn = new List<T>();
                }
                toReturn.Add(item);
            }
            yield return toReturn;
        }

        private IEnumerable<IAzureADObject> ToEntities(IEnumerable<DirectoryObject> fromGraph)
        {
            foreach (var directoryObj in fromGraph)
            {
                switch (directoryObj)
                {
                    case User user:
                        yield return new AzureADUser { ObjectId = Guid.Parse(user.Id) };
                        break;
                    case Group group:
                        yield return new AzureADGroup { ObjectId = Guid.Parse(group.Id) };
                        break;
                    default:
                        break;
                }
            }
        }

        private IEnumerable<AzureADUser> ToUsers(IEnumerable<DirectoryObject> fromGraph, Dictionary<string, int> nonUserGraphObjects)
        {
            foreach (var directoryObj in fromGraph)
            {
                switch (directoryObj)
                {
                    case User user:
                        yield return new AzureADUser { ObjectId = Guid.Parse(user.Id) };
                        break;
                    // We only care about users
                    // I'd prefer to be able to filter these out from the results on Graph's side, but the library doesn't support that yet.
                    // we do want to log the count of non-user graph objects, though
                    default:
                        if (nonUserGraphObjects.TryGetValue(directoryObj.ODataType, out int count))
                            nonUserGraphObjects[directoryObj.ODataType] = count + 1;
                        else
                            nonUserGraphObjects[directoryObj.ODataType] = 1;
                        break;
                }
            }
        }

        private async Task<bool> IsGroupOwnerAsync(string query, Guid groupObjectId)
        {
            try
            {
                var groupOwners = await _graphServiceClient.Groups[groupObjectId.ToString()].Owners.Request().WithMaxRetry(MaxRetries).Filter(query).GetAsync();
                await TrackMetrics(groupOwners.AdditionalData, QueryType.Other);
                return groupOwners.Any();
            }
            catch (ServiceException ex)
            {
                if (ex.StatusCode == HttpStatusCode.NotFound)
                    return false;

                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = ex.GetBaseException().ToString(),
                    RunId = RunId
                });

                throw;
            }
        }

        private AsyncRetryPolicy GetRetryPolicy()
        {
            var retryLimit = 4;
            var timeOutRetryLimit = 2;
            var currentRetryIndex = 0;
            var retryPolicy = Policy.Handle<ServiceException>(ex =>
            {
                if (ex.Message != null
                    && ex.Message.Contains("The request timed out")
                    && currentRetryIndex >= timeOutRetryLimit)
                {
                    return false;
                }

                return true;
            })
                    .WaitAndRetryAsync(
                       retryCount: retryLimit,
                       retryAttempt => TimeSpan.FromMinutes(2),
                       onRetry: async (ex, waitTime, retryIndex, context) =>
                       {
                           currentRetryIndex = retryIndex;

                           var currentLimit = retryLimit;
                           if (ex.Message != null && ex.Message.Contains("The request timed out"))
                           {
                               currentLimit = timeOutRetryLimit;
                           }

                           await _loggingRepository.LogMessageAsync(new LogMessage
                           {
                               Message = $"Got a transient exception. Retrying. This was try {retryIndex} out of {currentLimit}.\n{ex}"
                           });
                       }
                    );

            return retryPolicy;
        }
    }

    internal class RetryResponse
    {
        public string RequestId { get; set; }
        public ResponseCode ResponseCode { get; set; }
        public string AzureObjectId { get; set; }
    }

    public class ResourceUnitCustomEvent
    {
        public string TargetOfficeGroupId { get; set; } = "N/A";
        public string RunId { get; set; } = "N/A";
        public string ResourceUnit { get; set; } = "N/A";   
        public string QueryType { get; set; } = "N/A";
    }
}

