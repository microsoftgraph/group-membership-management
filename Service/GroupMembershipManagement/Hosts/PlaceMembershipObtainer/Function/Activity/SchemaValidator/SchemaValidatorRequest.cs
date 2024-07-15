// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace Hosts.PlaceMembershipObtainer
{
    public class SchemaValidatorRequest
    {
        public string Query { get; set; }
        public Guid? RunId { get; set; }
    }
}