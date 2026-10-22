// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Models.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tests.Services.MergeStreaming
{
    /// <summary>One page upload under the merge prefix: its blob name and its exact bytes.</summary>
    internal sealed record MergeUpload(string Name, byte[] Bytes);

    /// <summary>
    /// A deterministic merge input: the page uploads (in listing order) and the envelope fields passed to the merge.
    /// </summary>
    internal sealed record MergeCase(
        string Name,
        IReadOnlyList<MergeUpload> Uploads,
        AzureADGroup Destination,
        Guid RunId,
        Guid SyncJobId,
        bool Exclusionary,
        bool DryRun,
        string Query)
    {
        public const string Prefix = "11111111-2222-3333-4444-555555555555/userUploads/0f0e0d0c-0b0a-0908-0706-050403020100_GroupMembership_1_";
        public const string OutputPath = "/11111111-2222-3333-4444-555555555555/10202026-1200_0f0e0d0c-0b0a-0908-0706-050403020100_GroupMembership_1.json";

        public override string ToString() => Name;
    }

    /// <summary>
    /// Builds the golden merge inputs. Every case is fully deterministic (seeded identifiers, fixed strings), so the
    /// output bytes captured from the pre-streaming implementation stay valid across runs and machines.
    /// </summary>
    internal static class MergeGoldenCases
    {
        // Exactly the reader upload serializer settings (MembersReaderFunction and the other page readers).
        private static readonly JsonSerializerOptions _readerOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
        };

        private static readonly Guid _sourceGroupA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
        private static readonly Guid _sourceGroupB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");

        public static readonly string[] Names =
        {
            "EmptyNoUploads",
            "EmptyArrays",
            "SingleUpload",
            "DuplicatesAcrossUploads",
            "Large25k",
            "Unicode",
            "AllNullOptional",
        };

        /// <summary>
        /// Builds a case. With <paramref name="sortedPages"/> each page is stably sorted by the canonical comparer
        /// before it is serialized, as the page readers do now; without it the pages keep their generation order, as
        /// an older release wrote them. Both variants must merge to the same bytes.
        /// </summary>
        public static MergeCase Build(string name, bool sortedPages) => name switch
        {
            "EmptyNoUploads" => Case(name, new List<MergeUpload>(), exclusionary: false, dryRun: false, query: "[]"),
            "EmptyArrays" => Case(
                name,
                new List<MergeUpload>
                {
                    Raw("00000000-0000-0000-0000-000000000001.json", "[]"),
                    Raw("00000000-0000-0000-0000-000000000002.json", "[]"),
                },
                exclusionary: true,
                dryRun: false,
                query: "[{\"type\":\"GroupMembership\"}]"),
            "SingleUpload" => Case(
                name,
                Pages(sortedPages, Page(new Random(101), 0, 60, VariedUser)),
                exclusionary: false,
                dryRun: true,
                query: "[{\"type\":\"GroupMembership\",\"source\":\"aaaaaaaa-0000-0000-0000-00000000000a\"}]"),
            "DuplicatesAcrossUploads" => DuplicatesAcrossUploads(sortedPages),
            "Large25k" => Large25k(sortedPages),
            "Unicode" => Unicode(sortedPages),
            "AllNullOptional" => AllNullOptional(sortedPages),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
        };

        public static IEnumerable<object[]> AllNames() => Names.Select(n => new object[] { n });

        private static MergeCase Case(string name, List<MergeUpload> uploads, bool exclusionary, bool dryRun, string query) =>
            new(
                name,
                uploads,
                new AzureADGroup { ObjectId = Guid.Parse("11111111-2222-3333-4444-555555555555") },
                Guid.Parse("0f0e0d0c-0b0a-0908-0706-050403020100"),
                Guid.Parse("12345678-1234-1234-1234-123456789abc"),
                exclusionary,
                dryRun,
                query);

        private static MergeCase DuplicatesAcrossUploads(bool sortedPages)
        {
            var rng = new Random(202);
            var ids = Enumerable.Range(0, 40).Select(_ => NewId(rng)).ToArray();

            // Page 0: ids 0..14, plus a within-page duplicate of id 3 (the later record must lose).
            var page0 = Enumerable.Range(0, 15).Select(i => Marked(ids[i], $"p0-{i}", _sourceGroupA)).ToList();
            page0.Add(Marked(ids[3], "p0-3-second", _sourceGroupB));

            // Page 1: ids 10..29 (10..14 duplicate page 0; page 0 must win), plus a within-page duplicate.
            var page1 = Enumerable.Range(10, 20).Select(i => Marked(ids[i], $"p1-{i}", _sourceGroupB)).ToList();
            page1.Insert(5, Marked(ids[25], "p1-25-first", _sourceGroupA));

            // Page 2: ids 25..39 and 0..2 (both duplicate earlier pages).
            var page2 = Enumerable.Range(25, 15).Select(i => Marked(ids[i], $"p2-{i}", null)).ToList();
            page2.AddRange(Enumerable.Range(0, 3).Select(i => Marked(ids[i], $"p2-{i}", null)));

            // Page 3: only duplicates.
            var page3 = new List<AzureADUser> { Marked(ids[39], "p3-39", _sourceGroupA), Marked(ids[0], "p3-0", _sourceGroupB) };

            return Case("DuplicatesAcrossUploads", Pages(sortedPages, page0, page1, page2, page3), false, false, "[]");
        }

        private static MergeCase Large25k(bool sortedPages)
        {
            var rng = new Random(303);
            var ids = Enumerable.Range(0, 25_000).Select(_ => NewId(rng)).ToArray();
            var pages = new List<List<AzureADUser>>();
            for (var start = 0; start < ids.Length; start += 4_000)
            {
                pages.Add(Enumerable.Range(start, Math.Min(4_000, ids.Length - start))
                    .Select(i => i % 7 == 0 ? VariedUser(ids[i], i) : new AzureADUser { ObjectId = ids[i], SourceGroup = _sourceGroupA })
                    .ToList());
            }

            // About 2% duplicates, each copied into a different page with a different payload.
            for (var d = 0; d < 500; d++)
            {
                var j = rng.Next(ids.Length);
                var target = (j / 4_000 + 1 + rng.Next(pages.Count - 1)) % pages.Count;
                pages[target].Add(new AzureADUser { ObjectId = ids[j], DisplayName = $"dup-{d}", SourceGroup = _sourceGroupB });
            }

            return Case("Large25k", Pages(sortedPages, pages.ToArray()), false, false, "[{\"type\":\"GroupMembership\",\"source\":\"aaaaaaaa-0000-0000-0000-00000000000a\"}]");
        }

        private static MergeCase Unicode(bool sortedPages)
        {
            var rng = new Random(404);
            var strings = new[]
            {
                "José Müller",
                "日本語の名前",
                "emoji 😀👍🏽 family 👨‍👩‍👧",
                "quote \" backslash \\ slash / tab \t newline \n",
                "<script>alert('x')</script> & ampersand +plus",
                "control \u0001\u001f del \u007f",
                "lone-ish \u00a0 nbsp \u2028 line-sep \u2029 para-sep",
                "Ελληνικά العربية עברית",
                "",
            };

            AzureADUser Make(Guid id, int i) => new()
            {
                ObjectId = id,
                DisplayName = strings[i % strings.Length],
                Mail = $"user{i}@{(i % 2 == 0 ? "例え.jp" : "contoso.com")}",
                UserPrincipalName = strings[(i + 3) % strings.Length] + "@contoso.com",
                OnPremisesImmutableId = strings[(i + 5) % strings.Length],
                Properties = new Dictionary<string, object>
                {
                    ["name"] = strings[(i + 1) % strings.Length],
                    ["number"] = i * 1.5,
                    ["big"] = 12345678901234L,
                    ["nested"] = new Dictionary<string, object> { ["ü"] = new[] { "ä", "😀" }, ["flag"] = i % 2 == 0 },
                },
                MembershipAction = (MembershipAction)(i % 3),
                SourceGroup = i % 2 == 0 ? _sourceGroupA : Guid.Empty,
                SourceGroups = i % 4 == 0 ? new List<Guid> { _sourceGroupA, _sourceGroupB } : null,
            };

            var page0 = Enumerable.Range(0, 30).Select(i => Make(NewId(rng), i)).ToList();
            var page1 = Enumerable.Range(30, 30).Select(i => Make(NewId(rng), i)).ToList();
            page1.Add(Make(page0[4].ObjectId, 99)); // duplicate of a page-0 member with a different payload

            return Case("Unicode", Pages(sortedPages, page0, page1), true, true, "[{\"type\":\"GroupMembership\",\"source\":\"ü 😀 \\\"q\\\" <x>\"}]");
        }

        private static MergeCase AllNullOptional(bool sortedPages)
        {
            var rng = new Random(505);
            var ids = Enumerable.Range(0, 12).Select(_ => NewId(rng)).OrderBy(id => id, CanonicalObjectIdComparer.Instance).ToArray();

            // Only ObjectId is set, so the reader serializer omits every other property.
            var page0 = new List<AzureADUser>
            {
                new() { ObjectId = ids[5] },
                new() { ObjectId = ids[1] },
                new() { ObjectId = ids[9] },
                new() { ObjectId = Guid.Empty },
            };

            // Hand-written uploads (already canonically ordered, so both variants share them): explicit nulls, a null
            // element, lower-case property names (the merge reads case-insensitively) and extra whitespace.
            var explicitNulls = new StringBuilder("[");
            explicitNulls.Append("null,");
            explicitNulls.Append($"{{\"ObjectId\":\"{ids[0]}\",\"Mail\":null,\"UserPrincipalName\":null,\"DisplayName\":null,\"OnPremisesImmutableId\":null,\"Properties\":null,\"MembershipAction\":null,\"SourceGroup\":\"00000000-0000-0000-0000-000000000000\",\"SourceGroups\":null}},");
            explicitNulls.Append($" {{ \"objectid\" : \"{ids[2]}\" , \"displayname\" : null }} ,\n");
            explicitNulls.Append($"{{\"ObjectId\":\"{ids[3]}\",\"Properties\":{{}},\"SourceGroups\":[]}},");
            explicitNulls.Append("null,");
            explicitNulls.Append($"{{\"ObjectId\":\"{ids[5]}\",\"DisplayName\":\"loses to page 0\"}}");
            explicitNulls.Append(']');

            var uploads = Pages(sortedPages, page0);
            uploads.Add(Raw("ffffffff-0000-0000-0000-000000000001.json", explicitNulls.ToString()));
            uploads.Add(Raw("ffffffff-0000-0000-0000-000000000002.json", "  [ null ]  "));
            return Case("AllNullOptional", uploads, false, false, null);
        }

        private static AzureADUser VariedUser(Guid id, int i) => new()
        {
            ObjectId = id,
            DisplayName = i % 3 == 0 ? null : $"User {i}",
            Mail = i % 4 == 0 ? null : $"user{i}@contoso.com",
            UserPrincipalName = $"user{i}@contoso.com",
            OnPremisesImmutableId = i % 5 == 0 ? $"imm-{i}" : null,
            Properties = i % 6 == 0 ? new Dictionary<string, object> { ["k"] = i, ["d"] = i / 8.0 } : null,
            MembershipAction = i % 2 == 0 ? MembershipAction.Add : null,
            SourceGroup = i % 2 == 0 ? _sourceGroupA : Guid.Empty,
            SourceGroups = i % 9 == 0 ? new List<Guid> { _sourceGroupB } : null,
        };

        private static AzureADUser Marked(Guid id, string marker, Guid? sourceGroup) => new()
        {
            ObjectId = id,
            DisplayName = marker,
            Properties = new Dictionary<string, object> { ["marker"] = marker },
            SourceGroup = sourceGroup ?? Guid.Empty,
        };

        private static List<AzureADUser> Page(Random rng, int start, int count, Func<Guid, int, AzureADUser> make) =>
            Enumerable.Range(start, count).Select(i => make(NewId(rng), i)).ToList();

        private static List<MergeUpload> Pages(bool sortedPages, params List<AzureADUser>[] pages)
        {
            var uploads = new List<MergeUpload>();
            for (var p = 0; p < pages.Length; p++)
            {
                var members = pages[p];
                if (sortedPages)
                {
                    // Stable, as the readers sort: equal identifiers keep their relative order.
                    members = members.OrderBy(m => m, CanonicalMemberComparer<AzureADUser>.Instance).ToList();
                }

                // Names sort in page order, so the listing order is the page order.
                uploads.Add(new MergeUpload($"{p:D8}-0000-0000-0000-000000000000.json", JsonSerializer.SerializeToUtf8Bytes(members, _readerOptions)));
            }

            return uploads;
        }

        private static MergeUpload Raw(string name, string json) => new(name, Encoding.UTF8.GetBytes(json));

        private static Guid NewId(Random rng)
        {
            var bytes = new byte[16];
            rng.NextBytes(bytes);
            return new Guid(bytes);
        }
    }
}
