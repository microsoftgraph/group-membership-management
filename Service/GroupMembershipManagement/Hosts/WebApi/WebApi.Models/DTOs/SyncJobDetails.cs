// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace WebApi.Models.DTOs
{
    public class SyncJobDetails
    {
        public SyncJobDetails(
            DateTime startDate,
            DateTime lastSuccessfulStartTime,
            string query, 
            string requestor,
            int thresholdViolations, 
            int thresholdPercentageForAdditions,
            int thresholdPercentageForRemovals,
            List<string> endpoints,
            int period)
        { 
            StartDate = startDate;
            LastSuccessfulStartTime = lastSuccessfulStartTime;
            Query = query;
            Requestor = requestor;
            ThresholdViolations = thresholdViolations;
            ThresholdPercentageForAdditions = thresholdPercentageForAdditions;
            ThresholdPercentageForRemovals = thresholdPercentageForRemovals;
            Endpoints = endpoints;
            Period = period;
        }

        
        public DateTime StartDate { get; set; }
        public DateTime LastSuccessfulStartTime { get; set; }
        public string Query { get; set; }
        public string Requestor { get; set; }
        public int ThresholdViolations { get; set; }
        public int ThresholdPercentageForAdditions { get; set; }
        public int ThresholdPercentageForRemovals { get; set; }
        public List<string> Endpoints { get; set; }
        public int Period { get; set; }
        public string? TargetGroupName { get; set; }
        public Guid? TargetGroupId { get; set; }
        public string? TargetChannelName { get; set; }
        public string? TargetChannelId { get; set; }
        public Guid? SyncJobId { get; set; }
        public string? TargetGroupType { get; set; }
        public DateTime? LastSuccessfulRunTime { get; set; }
        public DateTime? EstimatedNextRunTime { get; set; }
        public string? Status { get; set; }
        public string? LastModifiedByDisplayName { get; set; }
        public string? LastModifiedByObjectId { get; set; }
        public string? LastModifiedOnBehalfOfDisplayName { get; set; }
        public string? LastModifiedOnBehalfOfObjectId { get; set; }
    }
}
