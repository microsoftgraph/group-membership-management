// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using SqlMembershipObtainer.Entities;
using System.Collections.Generic;

namespace Services.Tests.Helpers
{
    internal class OrganizationLevel
    {
        public int LevelId { get; set; }
        public List<PersonEntity> Entities { get; set; }
    }
}
