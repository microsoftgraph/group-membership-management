// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace Hosts.TeamsChannelUpdater
{
    public class EmailSenderRequest
    {
        public Guid RunId { get; set; }
        public string ToEmail { get; set; }
        public string ContentTemplate { get; set; }
        public string[] AdditionalContentParams { get; set; }
        public string CcEmail { get; set; }
    }
}