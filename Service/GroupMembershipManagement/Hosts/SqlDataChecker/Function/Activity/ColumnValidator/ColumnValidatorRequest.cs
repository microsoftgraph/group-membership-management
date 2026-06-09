// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;

namespace SqlDataChecker
{
    public class ColumnValidatorRequest
    {
        public List<string> Columns { get; set; }
        public string Table {  get; set; }
    }
}