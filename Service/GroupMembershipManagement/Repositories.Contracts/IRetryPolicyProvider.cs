// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Polly;
using System;
using System.Net.Http;

namespace Repositories.Contracts
{
    public interface IRetryPolicyProvider
    {
        AsyncPolicy<HttpResponseMessage> CreateRetryAfterPolicy(Guid? runId);
        AsyncPolicy<HttpResponseMessage> CreateExceptionHandlingPolicy(Guid? runId);

    }
}