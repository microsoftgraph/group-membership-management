// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Polly;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Repositories.Mocks
{
    public class MockNotificationTypesRepository : INotificationTypesRepository
    {

        private readonly Dictionary<string, int?> _notificationNameToIdMapping;

        public MockNotificationTypesRepository(Dictionary<string, int?> notificationNameToIdMapping = null)
        {
			_notificationNameToIdMapping = notificationNameToIdMapping ?? new Dictionary<string, int?>();
        }
        public async Task<int?> GetNotificationTypeIdByNotificationTypeName(string notificationName)
        {
            if (_notificationNameToIdMapping.TryGetValue(notificationName, out int? emailTypeId))
            {
                return await Task.FromResult(emailTypeId);
            }
            return null;
        }

    }
}