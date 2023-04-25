// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Graph;
using Microsoft.Graph.Models;
using Models;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Repositories.GraphGroups
{
    internal class GraphUserReader
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly GraphServiceClient _graphServiceClient;
        private readonly GraphGroupMetricTracker _graphGroupMetricTracker;

        public GraphUserReader(GraphServiceClient graphServiceClient,
                               ILoggingRepository loggingRepository,
                               GraphGroupMetricTracker graphGroupMetricTracker)
        {
            _graphServiceClient = graphServiceClient ?? throw new ArgumentNullException(nameof(graphServiceClient));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _graphGroupMetricTracker = graphGroupMetricTracker ?? throw new ArgumentNullException(nameof(graphGroupMetricTracker));
        }

        public async Task<List<AzureADUser>> GetTenantUsersAsync(int userCount, Guid? runId)
        {
            var tenantUsers = new HashSet<AzureADUser>();

            var userResponse = await _graphServiceClient.Users.GetAsync(requestConfiguration =>
            {
                requestConfiguration.QueryParameters.Top = userCount <= 999 ? userCount : 999;
            });

            var pageIterator = PageIterator<User, UserCollectionResponse>
                                .CreatePageIterator(
                                    _graphServiceClient,
                                    userResponse,
                                    (user) =>
                                    {
                                        tenantUsers.Add(new AzureADUser { ObjectId = new Guid(user.Id) });
                                        return tenantUsers.Count < userCount;
                                    }
                                );

            await pageIterator.IterateAsync();
            return tenantUsers.ToList();
        }

    }
}
