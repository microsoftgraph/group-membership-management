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
		private readonly Dictionary<string, NotificationType> _notificationNameToTypeMapping;

		public MockNotificationTypesRepository(Dictionary<string, NotificationType> notificationNameToTypeMapping = null)
		{
			_notificationNameToTypeMapping = notificationNameToTypeMapping ?? new Dictionary<string, NotificationType>();
		}

		public async Task<NotificationType> GetNotificationTypeByNotificationTypeNameAsync(string notificationName)
		{
			if (_notificationNameToTypeMapping.TryGetValue(notificationName, out NotificationType notificationType))
			{
				return await Task.FromResult(notificationType);
			}
			return null;
		}
	}
}