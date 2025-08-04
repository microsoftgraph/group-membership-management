// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Models.ServiceBus
{
    public class JobConfigurationQueueMessage
    {
        /// <summary>
        /// The unique identifier for the sync job
        /// </summary>
        public required Guid JobId { get; set; }

        /// <summary>
        /// The unique identifier for the group to be configured
        /// </summary>
        public required Guid GroupId { get; set; }
    }
}