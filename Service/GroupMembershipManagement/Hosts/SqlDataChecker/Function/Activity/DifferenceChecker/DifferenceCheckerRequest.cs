// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;

namespace SqlDataChecker
{
    public class DifferenceCheckerRequest
    {
        public Dictionary<string, int> LatestNullColumns { get; set; }
        public Dictionary<string, int> PreviousNullColumns {  get; set; }
        public int LatestNumberOfRows { get; set; }
        public int PreviousNumberOfRows { get; set; }
    }
}