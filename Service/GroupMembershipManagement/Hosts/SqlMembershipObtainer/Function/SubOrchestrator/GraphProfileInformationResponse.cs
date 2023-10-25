// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
namespace SqlMembershipObtainer.SubOrchestrator
{
    public class GraphProfileInformationResponse
    {
        /// <summary>
        /// Compressed serialized List<GraphProfileInformation>
        /// </summary>
        public string GraphProfiles { get; set; } = string.Empty;
        public int GraphProfileCount { get; set; }
    }
}
