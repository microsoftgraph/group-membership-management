// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class GetOnBehalfOfActivityRequest : RequestBase
    {
        public GetOnBehalfOfActivityRequest(int windowDays)
        {
            WindowDays = windowDays;
        }

        /// <summary>Number of UTC days to report on, inclusive of today.</summary>
        public int WindowDays { get; }
    }
}
