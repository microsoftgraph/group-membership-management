// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;

namespace SqlMembershipObtainer
{
    public class GroupMembershipSenderRequest
    {
        /// <summary>
        /// Compressed serialized List<GraphProfileInformation>
        /// </summary>
        public string Profiles { get; set; }
        public SyncJob SyncJob { get; set; }
        public int CurrentPart { get; set; }
        public bool Exclusionary { get; set; }
        public string AdaptiveCardTemplateDirectory { get; set; }
    }
}
