// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Hosts.MembershipAggregator
{
    public class AggregatedMembershipUploadResponse
    {
        public bool IsSuccessful { get; set; }
        public string FilePath { get; set; }
        public int MemberCount { get; set; }
        public string ErrorMessage { get; set; }
    }
}
