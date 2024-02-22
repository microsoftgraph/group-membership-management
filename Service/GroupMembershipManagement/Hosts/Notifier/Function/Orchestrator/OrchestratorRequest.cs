// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Hosts.Notifier
{
    public class OrchestratorRequest
    {
        public string MessageBody { get; set; }
        public string MessageType { get; set; }
        public string SubjectTemplate { get; set; }
        public string ContentTemplate { get; set; }
    }
}
