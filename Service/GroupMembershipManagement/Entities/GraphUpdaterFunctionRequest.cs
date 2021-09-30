// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace Entities
{
    public class GraphUpdaterFunctionRequest
    {
        public Guid RunId { get; set; }
        public string Message { get; set; }
        public string MessageSessionId { get; set; }
        public string MessageLockToken { get; set; }
        public bool IsCancelationRequest { get; set; }
    }
}
