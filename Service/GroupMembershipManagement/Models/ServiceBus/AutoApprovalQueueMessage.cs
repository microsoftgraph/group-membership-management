// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Models.ServiceBus
{
    public class AutoApprovalQueueMessage
    {
        /// <summary>
        /// The unique identifier for the sync job.
        /// </summary>
        public required Guid SyncJobId { get; init; }

        /// <summary>
        /// Object ID of the requestor.
        /// </summary>
        public required string RequestorObjectId { get; init; }

        /// <summary>
        /// Display name of the requestor.
        /// </summary>
        public string? RequestorDisplayName { get; init; }

        /// <summary>
        /// Display name if the request was submitted on behalf of another user.
        /// </summary>
        public string? ChangedOnBehalfOfDisplayName { get; init; }

        /// <summary>
        /// Object ID if the request was submitted on behalf of another user.
        /// </summary>
        public string? ChangedOnBehalfOfObjectId { get; init; }

        /// <summary>
        /// Business justification provided for the request.
        /// </summary>
        public string? BusinessJustification { get; init; }
    }
}