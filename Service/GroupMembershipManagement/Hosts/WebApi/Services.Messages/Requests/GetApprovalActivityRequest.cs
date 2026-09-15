// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class GetApprovalActivityRequest : RequestBase
    {
        public GetApprovalActivityRequest(int windowDays)
        {
            WindowDays = windowDays;
        }

        /// <summary>Number of UTC days to report on, inclusive of today.</summary>
        public int WindowDays { get; }
    }
}
