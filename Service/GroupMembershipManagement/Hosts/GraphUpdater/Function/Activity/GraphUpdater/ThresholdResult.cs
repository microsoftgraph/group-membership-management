// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
namespace GraphUpdater.GraphUpdaterActivity
{
    class ThresholdResult
    {
        public double IncreaseThresholdPercentage { get; set; }
        public double DecreaseThresholdPercentage { get; set; }
        public bool IsAdditionsThresholdExceeded { get; set; }
        public bool IsRemovalsThresholdExceeded { get; set; }
        public bool IsThresholdExceeded => IsAdditionsThresholdExceeded || IsRemovalsThresholdExceeded;
    }
}
