// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using Models;
using Models.Helpers;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using Services.WebApi.Contracts;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Services
{
    public partial class GetRunExplanationHandler
    {
        public const int MaxUsersPerRunExplanation = 150;

        private async Task<(List<Guid> Added, List<Guid> Removed)> ReadMembershipDeltaAsync(string targetGroupId, Guid runId)
        {
            var empty = (new List<Guid>(), new List<Guid>());

            var blobResult = await _blobStorageRepository.FindAggregatedFileByRunIdAsync(targetGroupId, runId.ToString());
            if (blobResult.BlobStatus == BlobStatus.NotFound || string.IsNullOrWhiteSpace(blobResult.Path))
            {
                return empty;
            }

            var fileContent = await _blobStorageRepository.DownloadFileAsync(blobResult.Path);
            if (fileContent.BlobStatus == BlobStatus.NotFound || string.IsNullOrWhiteSpace(fileContent.Content))
            {
                return empty;
            }

            var json = TryDecompress(fileContent.Content);
            if (string.IsNullOrWhiteSpace(json))
            {
                return empty;
            }

            return ParseMembershipDelta(json);
        }

        // Forward-only Utf8JsonReader parse; ref struct prohibits use inside async, so this lives in a sync helper.
        public static (List<Guid> Added, List<Guid> Removed) ParseMembershipDelta(string json)
        {
            var added = new List<Guid>();
            var removed = new List<Guid>();

            if (string.IsNullOrWhiteSpace(json))
            {
                return (added, removed);
            }

            try
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });

                if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                {
                    return (added, removed);
                }

                // Scan root-level properties for "SourceMembers"
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                    {
                        return (added, removed);
                    }

                    if (reader.TokenType == JsonTokenType.PropertyName &&
                        (reader.ValueTextEquals("SourceMembers"u8) || reader.ValueTextEquals("sourceMembers"u8)))
                    {
                        break;
                    }

                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        reader.Read();
                        reader.TrySkip();
                    }
                }

                if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
                {
                    return (added, removed);
                }

                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndArray)
                    {
                        break;
                    }

                    if (reader.TokenType != JsonTokenType.StartObject)
                    {
                        reader.TrySkip();
                        continue;
                    }

                    Guid? objectId = null;
                    MembershipAction? action = null;

                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        if (reader.TokenType != JsonTokenType.PropertyName) continue;

                        if (reader.ValueTextEquals("ObjectId"u8) || reader.ValueTextEquals("objectId"u8))
                        {
                            if (reader.Read() && reader.TokenType == JsonTokenType.String &&
                                Guid.TryParse(reader.GetString(), out var id))
                            {
                                objectId = id;
                            }
                        }
                        else if (reader.ValueTextEquals("MembershipAction"u8) || reader.ValueTextEquals("membershipAction"u8))
                        {
                            if (reader.Read())
                            {
                                if (reader.TokenType == JsonTokenType.Number &&
                                    reader.TryGetInt32(out var actionInt) &&
                                    Enum.IsDefined(typeof(MembershipAction), actionInt))
                                {
                                    action = (MembershipAction)actionInt;
                                }
                                else if (reader.TokenType == JsonTokenType.String &&
                                         Enum.TryParse<MembershipAction>(reader.GetString(), true, out var actionEnum))
                                {
                                    action = actionEnum;
                                }
                            }
                        }
                        else
                        {
                            reader.Read();
                            reader.TrySkip();
                        }
                    }

                    if (objectId.HasValue && action.HasValue)
                    {
                        if (action.Value == MembershipAction.Add) added.Add(objectId.Value);
                        else if (action.Value == MembershipAction.Remove) removed.Add(objectId.Value);
                    }
                }
            }
            catch (JsonException) { /* fall through with whatever we got */ }
            catch (InvalidOperationException) { /* fall through with whatever we got */ }

            return (added, removed);
        }

        private static string TryDecompress(string content)
        {
            try
            {
                return TextCompressor.Decompress(content);
            }
            catch (FormatException)
            {
                return content;
            }
        }

        // Cap combined users at MaxUsersPerRunExplanation, biased toward keeping both sides represented
        // when one side dominates. If total <= cap, returns inputs unchanged.
        public static (List<Guid> Added, List<Guid> Removed) CapAt150(IReadOnlyList<Guid> added, IReadOnlyList<Guid> removed)
        {
            int total = added.Count + removed.Count;
            if (total <= MaxUsersPerRunExplanation)
            {
                return (added.ToList(), removed.ToList());
            }

            int half = MaxUsersPerRunExplanation / 2;
            int addedTake;
            int removedTake;

            if (added.Count <= half)
            {
                addedTake = added.Count;
                removedTake = MaxUsersPerRunExplanation - addedTake;
            }
            else if (removed.Count <= half)
            {
                removedTake = removed.Count;
                addedTake = MaxUsersPerRunExplanation - removedTake;
            }
            else
            {
                addedTake = half;
                removedTake = MaxUsersPerRunExplanation - addedTake;
            }

            return (added.Take(addedTake).ToList(), removed.Take(removedTake).ToList());
        }
    }
}
