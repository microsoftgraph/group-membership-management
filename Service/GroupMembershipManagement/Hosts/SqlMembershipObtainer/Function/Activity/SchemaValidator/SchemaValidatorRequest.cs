// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace SqlMembershipObtainer
{
    public class SchemaValidatorRequest
    {
        public required string Query { get; init; }
        public required Guid RunId { get; init; }
    }
}