// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Repositories.Contracts.InjectConfig
{
    public interface IGraphServiceAttemptsValue
    {
        int MaxRetryAfterAttempts { get; }
        int MaxExceptionHandlingAttempts { get; }

    }
}