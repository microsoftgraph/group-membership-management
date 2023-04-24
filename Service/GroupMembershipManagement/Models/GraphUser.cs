// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Models
{
    public class GraphUser
    {
        public string DisplayName { get; set; }
        public bool? AccountEnabled { get; set; }
        public string Password { get; set; }
        public string MailNickname { get; set; }
        public string UsageLocation { get; set; }
        public string UserPrincipalName { get; set; }
        public string OnPremisesImmutableId { get; set; }
    }
}
