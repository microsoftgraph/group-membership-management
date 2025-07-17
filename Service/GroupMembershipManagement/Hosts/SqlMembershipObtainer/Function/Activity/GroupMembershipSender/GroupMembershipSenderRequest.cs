// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;

namespace SqlMembershipObtainer
{
    public class GroupMembershipSenderRequest
    {
        /// <summary>
        /// Compressed serialized List<GraphProfileInformation>
        /// </summary>
        public required string Profiles { get; init; }
        public required SyncJob SyncJob { get; init; }
        public required Guid GroupId { get; init; }
        public required int CurrentPart { get; init; }
        public required bool Exclusionary { get; init; }
        public required string AdaptiveCardTemplateDirectory { get; init; }
    }
}
