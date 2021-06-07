// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using AzureUserReader.Requests;
using Entities;
using System.Collections.Generic;

namespace AzureUserReader.UploadUsers
{
    public class UploadUsersRequest
    {
        public AzureUserReaderRequest AzureUserReaderRequest { get; set; }
        public IList<GraphProfileInformation> Users { get; set; }
    }
}
