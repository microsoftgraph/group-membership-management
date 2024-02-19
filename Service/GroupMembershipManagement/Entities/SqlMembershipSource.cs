// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;
using System.Collections.Generic;

namespace Entities
{
    public class SqlMembershipSource
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string CustomLabel { get; set; }
        public List<SqlFilterAttribute> Attributes { get; set; }
    }
}