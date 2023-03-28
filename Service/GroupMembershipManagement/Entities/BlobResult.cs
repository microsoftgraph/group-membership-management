// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;

namespace Entities
{
    public class BlobResult : BlobMetadataResult
    {
        public BinaryData Content { get; set; }
    }
}
