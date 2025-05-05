// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace WebApi.Models.DTOs
{
    public class GroupSettings
    {
        public List<AuthorizedSender>? AuthorizedSenders { get; set; }
        public bool? HiddenFromExchangeClients { get; set; }
        public bool? WelcomeMessageEnabled { get; set; }
    }
}
