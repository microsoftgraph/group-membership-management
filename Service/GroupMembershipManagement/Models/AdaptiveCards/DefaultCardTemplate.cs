// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Models.AdaptiveCards
{
    public class DefaultCardTemplate
    {
        public string GroupId { get; set; }
        public string ProviderId { get; set; }
        public string SubjectContent { get; set; }
        public string MessageContent { get; set; }
        public DateTime CardCreatedTime { get; set; }
        public string DestinationGroupName { get; set; }
        public string UIUrl { get; set; }
        public string DashboardUrl { get; set; }
    }
}
               