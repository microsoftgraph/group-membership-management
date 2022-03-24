// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;

namespace Entities
{
    public class BlobMetadataResult
    {
        public IDictionary<string, string> Metadata { get; set; }
        public BlobStatus BlobStatus { get; set; }
    }
}
