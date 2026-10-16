// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Services.Messages.Contracts.Responses;

namespace Services.Messages.Responses
{
    public class GetConfiguredAttributeApprovalResponse : ResponseBase
    {
        public ConfiguredAttributeApprovalConfiguration Configuration { get; set; } = new();
    }
}
