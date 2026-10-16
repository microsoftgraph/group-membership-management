// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class SaveConfiguredAttributeApprovalRequest : RequestBase
    {
        public SaveConfiguredAttributeApprovalRequest(ConfiguredAttributeApprovalConfiguration configuration)
        {
            Configuration = configuration;
        }

        public ConfiguredAttributeApprovalConfiguration Configuration { get; }
    }
}
