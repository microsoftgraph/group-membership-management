// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Kiota.Abstractions;
using System;

namespace Repositories.GraphAgents
{
    public sealed record AgentGraphRequestContext(Guid RunId) : IRequestOption;
}
