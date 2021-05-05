// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts.InjectConfig;

namespace DIConcreteTypes
{
    public class GraphServiceAttemptsValue : IGraphServiceAttemptsValue
    {
        public int MaxRetryAfterAttempts { get; set; }

        public int MaxExceptionHandlingAttempts { get; set; }

        public GraphServiceAttemptsValue(int maxRetryAfterAttempts, int maxExceptionHandlingAttempts)
        {
            this.MaxRetryAfterAttempts = maxRetryAfterAttempts;
            this.MaxExceptionHandlingAttempts = maxExceptionHandlingAttempts;
        }

        public GraphServiceAttemptsValue()
        {

        }
    }
}

