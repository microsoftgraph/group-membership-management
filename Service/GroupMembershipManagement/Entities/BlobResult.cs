// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace Entities
{
    public class BlobResult : BlobMetadataResult
    {
        public BinaryData Content { get; set; }
    }
}
