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

        public string GetQuery(int partIndex)
        {
            var queryPart = QueryParts.First(x => x.Index == partIndex);
            return $"{{'type':'{queryPart.Type}','source': '{queryPart.SourceId}'}}";
        }

        public string GetSourceIds(int partIndex)
        {
            return string.Join(";", QueryParts[partIndex].SourceId);
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
    }
}
