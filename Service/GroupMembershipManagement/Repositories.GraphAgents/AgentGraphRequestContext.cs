// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Kiota.Abstractions;
using Models;
using System;

namespace Repositories.GraphAgents
{
    public sealed record AgentGraphRequestContext(Guid RunId, QueryType QueryType = QueryType.AgentUser) : IRequestOption;
}
