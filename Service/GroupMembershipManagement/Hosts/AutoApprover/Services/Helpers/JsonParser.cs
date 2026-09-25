// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Services.AutoApprover.Helpers
{
    internal static class JsonParser
    {
        /// <summary>
        /// Strict-shape parser producing a heterogeneous list of source parts for the Per-Part Rule.
        /// Returns <c>false</c> (with an empty list) for the whole query on any shape failure, and
        /// reports which failure via <paramref name="failure"/> so the decline log can name the cause.
        /// A SQL part without a <c>manager.id</c> is a supported GMM query shape, so it parses
        /// successfully and is rejected individually by the evaluator rather than discarding the
        /// whole query. <c>filter</c> is captured for sensitive-attribute screening only;
        /// <c>manager.depth</c> and <c>exclusionary</c> are ignored and never cause a parse failure.
        /// </summary>
        internal static bool TryParseParts(string query, out List<SourcePart> parts, out QueryParseFailure failure)
        {
            parts = new List<SourcePart>();
            failure = QueryParseFailure.None;

            if (string.IsNullOrWhiteSpace(query))
            {
                failure = QueryParseFailure.QueryEmpty;
                return false;
            }

            try
            {
                var queryArray = JsonNode.Parse(query)?.AsArray();
                if (queryArray == null)
                {
                    failure = QueryParseFailure.NotAJsonArray;
                    return false;
                }

                foreach (var item in queryArray)
                {
                    var sourceObject = item?.AsObject();
                    if (sourceObject == null)
                    {
                        failure = QueryParseFailure.PartNotAnObject;
                        return false;
                    }

                    var typeValue = sourceObject["type"]?.GetValue<string>();

                    if (typeValue == SourcePart.GroupMembershipType)
                    {
                        var sourceValue = sourceObject["source"]?.GetValue<string>();
                        if (string.IsNullOrWhiteSpace(sourceValue) || !Guid.TryParse(sourceValue, out _))
                        {
                            failure = QueryParseFailure.GroupSourceNotAGroupId;
                            return false;
                        }

                        parts.Add(new SourcePart
                        {
                            Type = SourcePart.GroupMembershipType,
                            SourceGroupId = sourceValue
                        });
                    }
                    else if (typeValue == SourcePart.SqlMembershipType)
                    {
                        var sqlSource = sourceObject["source"]?.AsObject();
                        if (sqlSource == null)
                        {
                            failure = QueryParseFailure.SqlSourceMissing;
                            return false;
                        }

                        // A manager-less SqlMembership source (filter-only) is a valid, supported GMM
                        // query shape, so it must not fail the parse for the whole submission. It is
                        // kept with a null ManagerId and rejected as a single part by the evaluator,
                        // which reports why that specific part is not eligible for auto-approval.
                        int? managerId = sqlSource["manager"]?.AsObject()?["id"]?.GetValue<int?>();

                        // A non-string filter must not fail the parse; null means nothing to screen.
                        string filter = null;
                        try
                        {
                            filter = sqlSource["filter"]?.GetValue<string>();
                        }
                        catch (InvalidOperationException)
                        {
                        }

                        parts.Add(new SourcePart
                        {
                            Type = SourcePart.SqlMembershipType,
                            ManagerId = managerId,
                            Filter = string.IsNullOrWhiteSpace(filter) ? null : filter
                        });
                    }
                    else
                    {
                        failure = QueryParseFailure.UnsupportedSourceType;
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                parts = new List<SourcePart>();
                failure = QueryParseFailure.MalformedJson;
                return false;
            }
        }
    }
}