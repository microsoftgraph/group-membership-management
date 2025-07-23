// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using SqlMembershipObtainer.Entities;
using System;

namespace SqlMembershipObtainer
{
    public class OrganizationProcessorRequest
    {
        public required Query Query { get; init; }
        public required SyncJob SyncJob { get; init; }
        public required Guid GroupId { get; init; }
        public required int CurrentPart { get; init; }
        public required bool Exclusionary { get; init; }
        public required string AdaptiveCardTemplateDirectory { get; init; }
    }
}
