// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;

namespace Repositories.GraphGroups
{
    internal class GraphObjectResponse<T>
    {
        public T Response { get; set; } = default;
        public IDictionary<string, IEnumerable<string>> Headers { get; set; } = new Dictionary<string, IEnumerable<string>>();

    }
}
