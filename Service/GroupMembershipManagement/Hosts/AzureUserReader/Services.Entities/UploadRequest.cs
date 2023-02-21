// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System.Collections.Generic;

namespace Services.Entities
{
    public class UploadRequest
    {
        public string ContainerName { get; set; }
        public string BlobTargetDirectory{ get; set; }
        public IList<GraphProfileInformation> Users { get; set; }
    }
}
