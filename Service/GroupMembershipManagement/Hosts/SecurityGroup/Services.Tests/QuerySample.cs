// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Tests.Services
{
    public class QuerySample
    {
        public List<QueryPart> QueryParts { get; set; } = new List<QueryPart>();

        public string GetQuery()
        {
            var allParts = QueryParts.Select(x => $"{{'type':'{x.Type}','sources': [{string.Join(",", x.SourceIds.Select(id => $"'{id}'"))}]}}");
            return $"[{string.Join(",", allParts)}]";
        }

        public string GetSourceIds(int partIndex)
        {
            return string.Join(";", QueryParts[partIndex].SourceIds);
        }

        public static QuerySample GenerateQuerySample(string syncType, int numberOfParts = 2)
        {
            var sampleQuery = new QuerySample();
            for (int i = 0; i < numberOfParts; i++)
            {
                sampleQuery.QueryParts.Add(new QueryPart
                {
                    Index = i,
                    Type = syncType,
                    SourceIds = Enumerable.Range(0, 3).Select(x => Guid.NewGuid()).ToList()
                });

            }

            return sampleQuery;
        }
    }

    public class QueryPart
    {
        public int Index { get; set; }
        public string Type { get; set; }
        public List<Guid> SourceIds { get; set; }
    }
}
