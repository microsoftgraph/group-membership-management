// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Services.Messages.Contracts.Responses;

namespace Services.Messages.Responses
{
    public class GetGroupOnboardingStatusResponse : ResponseBase
    {
        public OnboardingStatus Status { get; set; }
    }
}
