// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System.Collections.Generic;

namespace Hosts.AzureUserReader
{
    public class UploadUsersRequest
    {
        public AzureUserReaderRequest AzureUserReaderRequest { get; set; }
        public IList<GraphProfileInformation> Users { get; set; }
    }
}
