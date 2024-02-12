// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class PatchDefaultSqlMembershipSourceCustomLabelRequest : RequestBase
    {
        public string CustomLabel { get; }
        public PatchDefaultSqlMembershipSourceCustomLabelRequest(string customLabel)
        {
            CustomLabel = customLabel;
        }
    }
}