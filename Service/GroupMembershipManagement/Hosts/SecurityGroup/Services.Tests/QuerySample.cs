// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Collections.Generic;
using System.Linq;

namespace Tests.Services
{
    public class QuerySample
    {
        public List<QueryPart> QueryParts { get; set; } = new List<QueryPart>();

        public string GetQuery()
        {
            var allParts = QueryParts.Select(x => $"{{'type':'{x.Type}','sources': [{string.Join(",", x.SourceIds)}]}}");
            return $"[{string.Join(",", allParts)}]";
        }

        public string GetSourceIds(int partIndex)
        {
            return string.Join(";", QueryParts[partIndex].SourceIds);
        }
    }

    public class QueryPart
    {
        public int Index { get; set; }
        public string Type { get; set; }
        public string Query { get; }
        public List<string> SourceIds { get; set; }
    }
}
