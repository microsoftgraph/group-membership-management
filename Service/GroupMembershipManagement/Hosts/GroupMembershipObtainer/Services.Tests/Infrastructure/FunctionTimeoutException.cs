// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

// Test-only stand-in with the exact type name the Functions host records when an activity runs past functionTimeout
// (measured locally, 2026-10-21: TaskFailedException.FailureDetails.ErrorType is
// "Microsoft.Azure.WebJobs.Host.FunctionTimeoutException"). The isolated worker does not reference the host assembly, so
// the tests throw this type through ActivityCallWrapper to produce the same failure details.
namespace Microsoft.Azure.WebJobs.Host
{
    internal sealed class FunctionTimeoutException : Exception
    {
        public FunctionTimeoutException(string message)
            : base(message)
        {
        }
    }
}
