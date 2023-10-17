// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace WebApi.Models.DTOs
{
    public class SyncJobPatch
    {
        public Guid? RunId { get; set; }
        public string Requestor { get; set; }
        public Guid TargetOfficeGroupId { get; set; }
        public string Destination { get; set; }
        public bool AllowEmptyDestination { get; set; }
        public string Status { get; set; }
        public DateTime LastRunTime { get; set; }
        public DateTime LastSuccessfulRunTime { get; set; }
        public DateTime LastSuccessfulStartTime { get; set; }
        public int Period { get; set; }
        public string Query { get; set; }
        public DateTime StartDate { get; set; }
        public bool IgnoreThresholdOnce { get; set; }
        public int ThresholdPercentageForAdditions { get; set; }
        public int ThresholdPercentageForRemovals { get; set; }
        public bool IsDryRunEnabled { get; set; }
        public DateTime DryRunTimeStamp { get; set; }
        public int ThresholdViolations { get; set; }
    }
}
