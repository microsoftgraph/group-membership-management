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
            var parts = string.Join(",", QueryParts.OrderBy(x => x.Index).Select(x => GetPartQuery(x.Index)));
            return $"[{parts}]";
        }

        private string GetPartQuery(int partIndex)
        {
            var queryPart = QueryParts.First(x => x.Index == partIndex);
            return $"{{'type':'{queryPart.Type}','source': '{queryPart.SourceId}'}}";
        }

        public Guid GetSourceId(int partIndex)
        {
            return QueryParts.First(x => x.Index == partIndex).SourceId;
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
                    SourceId = Guid.NewGuid()
                });
            }

            return sampleQuery;
        }
    }

    public class QueryPart
    {
        public int Index { get; set; }
        public string Type { get; set; }
        public Guid SourceId { get; set; }
        public bool IsDestinationPart { get; set; }
    }
}
