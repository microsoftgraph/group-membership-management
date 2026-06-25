// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;
using System;

namespace Services.Messages.Requests
{
    public class SpotCheckUserMembershipRequest : RequestBase
    {
        public SpotCheckUserMembershipRequest(Guid syncJobId, string userId)
        {
            SyncJobId = syncJobId;
            UserId = userId;
        }

        public Guid SyncJobId { get; }

        public string UserId { get; }
    }
}
