// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;

namespace Models
{
    public class Page<T>
    {
        public string Query { get; set; }
        public string ContinuationToken { get; set; }
        public IReadOnlyList<T> Values { get; set; } = new List<T>();
    }
}
