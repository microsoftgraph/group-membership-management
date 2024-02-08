// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Services.Messages.Contracts.Requests;
using System;

namespace Services.Messages.Requests
{
    public class GetOrgLeaderDetailsRequest : RequestBase
    {
        public GetOrgLeaderDetailsRequest(string objectId)
        {
            ObjectId = objectId;
        }

        public string ObjectId { get; }
    }
}