// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;

namespace WebApi.Models
{
    public class OperationDetails
    {
        public Operations Operation { get; set; }
        public Guid RequestorId { get; set; }
    }
}
