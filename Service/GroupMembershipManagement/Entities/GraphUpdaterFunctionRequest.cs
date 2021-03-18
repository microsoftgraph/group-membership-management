// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Diagnostics.CodeAnalysis;

namespace Entities
{
    [ExcludeFromCodeCoverage]
    public class GraphUpdaterFunctionRequest
    {
        public string Message { get; set; }
        public string MessageSessionId { get; set; }
        public string MessageLockToken { get; set; }

    }
}
