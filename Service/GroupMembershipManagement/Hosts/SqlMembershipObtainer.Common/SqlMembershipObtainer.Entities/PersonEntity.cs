// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Diagnostics.CodeAnalysis;

namespace SqlMembershipObtainer.Entities
{
    [ExcludeFromCodeCoverage]
    public class PersonEntity
    {
        public string PersonnelNumber { get; set; }
        public string AzureObjectId { get; set; }
    }
}
