// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using static Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace WebApi.Tests.ExceptionHandling
{
    /// <summary>
    /// Shared assertions for verifying that a sanitized error response body does
    /// not leak any details from the underlying thrown exception (message,
    /// type name, or stack frames from internal assemblies).
    /// </summary>
    public static class AssertNoExceptionLeak
    {
        public static void Assert(string responseBody, Exception thrown)
        {
            if (!string.IsNullOrEmpty(thrown.Message))
            {
                IsFalse(
                    responseBody.Contains(thrown.Message),
                    "Response body must not contain the exception Message.");
            }

            var typeName = thrown.GetType().FullName;
            if (!string.IsNullOrEmpty(typeName))
            {
                IsFalse(
                    responseBody.Contains(typeName),
                    "Response body must not contain the exception type name.");
            }

            IsFalse(
                responseBody.Contains("at WebApi."),
                "Response body must not contain a stack frame from the WebApi assembly.");

            IsFalse(
                responseBody.Contains("at Services."),
                "Response body must not contain a stack frame from the Services assembly.");
        }
    }
}
