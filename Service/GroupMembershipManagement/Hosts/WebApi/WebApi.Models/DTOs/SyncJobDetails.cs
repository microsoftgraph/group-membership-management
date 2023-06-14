// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace WebApi.Models.DTOs
{
    public class SyncJobDetails
    {
        public SyncJobDetails(
            DateTime startDate,
            DateTime lastSuccessfulStartTime,
            string source, 
            string requestor,
            int thresholdViolations, 
            int thresholdPercentageForAdditions,
            int thresholdPercentageForRemovals)
        { 
            StartDate = startDate;
            LastSuccessfulStartTime = lastSuccessfulStartTime;
            Source = source;
            Requestor = requestor;
            ThresholdViolations = thresholdViolations;
            ThresholdPercentageForAdditions = thresholdPercentageForAdditions;
            ThresholdPercentageForRemovals = thresholdPercentageForRemovals;
        }

        
        public DateTime StartDate { get; set; }
        public DateTime LastSuccessfulStartTime { get; set; }
        public string Source { get; set; }
        public string Requestor { get; set; }
        public int ThresholdViolations { get; set; }
        public int ThresholdPercentageForAdditions { get; set; }
        public int ThresholdPercentageForRemovals { get; set; }
    }
}
