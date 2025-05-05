// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace WebApi.Models.DTOs
{
    public class NewSyncJob
    {
        public NewSyncJob()
        {
        }
        public string Status { get; set; }
        public int Period { get; set; }
        public string Query { get; set; }
        public string Requestor { get; set; }
        public string LastModifiedOnBehalfOfDisplayName { get; set; }
        public string LastModifiedOnBehalfOfObjectId { get; set; }
        public int ThresholdPercentageForAdditions { get; set; }
        public int ThresholdPercentageForRemovals { get; set; }
        public string StartDate { get; set; }
        public string Destination { get; set; }
        public string BusinessJustification { get; set; }
        public GroupSettings? GroupSettings { get; set; }

    }
}
