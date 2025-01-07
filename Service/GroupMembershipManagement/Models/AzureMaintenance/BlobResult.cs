// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace Models
{
    public class BlobResult : BlobMetadataResult
    {
        public string Content { get; set; }
        public DateTimeOffset? LastModified { get; set; }
    }
}
