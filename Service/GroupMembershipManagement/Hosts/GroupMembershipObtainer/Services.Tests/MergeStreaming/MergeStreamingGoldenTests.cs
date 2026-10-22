// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Helpers;
using Models.ServiceBus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Tests.Services.Infrastructure;

namespace Tests.Services.MergeStreaming
{
    /// <summary>
    /// Byte parity of the page-upload merge (<c>MergeAndStreamUserBlobsAsync</c> and its write-once form). The
    /// golden SHA-256 values and lengths were captured from the materializing implementation (list, dedupe, sort,
    /// <c>JsonSerializer.Serialize(groupMembership)</c>) before the streaming merge replaced it, over the inputs in
    /// <see cref="MergeGoldenCases"/>. The pages are written both canonically sorted (as the readers now write them,
    /// the streaming path) and in generation order (as an older release wrote them, the fallback path); both must
    /// produce the golden bytes, from the production repository and from the in-memory test double.
    /// </summary>
    [TestClass]
    public class MergeStreamingGoldenTests
    {
        private static readonly Dictionary<string, (int Count, int Length, string Sha256)> _goldens = new()
        {
            ["EmptyNoUploads"] = (0, 470, "73508DE717463AE3F909E2EE24E36C103BF220B9C512ADD5C8F914F5F8241A75"),
            ["EmptyArrays"] = (0, 515, "0A8DE0C11E19F1AE7510EC01620AA261B21EEA302360F735B28CD73B7A0CC213"),
            ["SingleUpload"] = (60, 17884, "A59DBD8C381B591B1569F6B62EC3E7BFFAF3C5946D73480658A2C47790778DA1"),
            ["DuplicatesAcrossUploads"] = (40, 11221, "9A995E5608DF5EB8E2F41280063441B998760BC154FD4CB011C156EB67188736"),
            ["Large25k"] = (25000, 6457884, "BD1D6292DF980FB2D1BB25913A18F22D65378C6B0CE08B6E2A80CE10DC339E3F"),
            ["Unicode"] = (60, 37236, "3C8815BFD6CF0F8B984E49576457D7A7F0FC00F4775CFCBF5A264AD145727450"),
            ["AllNullOptional"] = (7, 2229, "F876666606988BD1BF988C06915316DE5742F6F8341B4630744E2A2BEE8DF7E4"),
        };

        public static IEnumerable<object[]> Variants() =>
            from name in MergeGoldenCases.Names
            from sorted in new[] { true, false }
            from writeOnce in new[] { false, true }
            select new object[] { name, sorted, writeOnce };

        [DataTestMethod]
        [DynamicData(nameof(Variants), DynamicDataSourceType.Method)]
        public async Task ProductionMerge_MatchesGoldenBytes(string name, bool sortedPages, bool writeOnce)
        {
            var mergeCase = MergeGoldenCases.Build(name, sortedPages);
            var fake = new AzureBlobContainerFake();
            foreach (var upload in mergeCase.Uploads)
            {
                fake.Put(MergeCase.Prefix + upload.Name, upload.Bytes);
            }

            var logger = new RecordingLogger<MergeStreamingGoldenTests>();
            var repository = fake.CreateRepository(logger);
            var count = writeOnce
                ? (await repository.MergeAndStreamUserBlobsWriteOnceAsync(
                    MergeCase.Prefix, MergeCase.OutputPath, mergeCase.Destination, mergeCase.RunId, mergeCase.SyncJobId,
                    mergeCase.Exclusionary, mergeCase.DryRun, mergeCase.Query)).Count
                : await repository.MergeAndStreamUserBlobsAsync(
                    MergeCase.Prefix, MergeCase.OutputPath, mergeCase.Destination, mergeCase.RunId, mergeCase.SyncJobId,
                    mergeCase.Exclusionary, mergeCase.DryRun, mergeCase.Query);

            AssertGolden(mergeCase, count, fake.GetBytes(MergeCase.OutputPath));
            AssertPath(mergeCase, logger);
        }

        [DataTestMethod]
        [DynamicData(nameof(Variants), DynamicDataSourceType.Method)]
        public async Task InMemoryDoubleMerge_MatchesGoldenBytes(string name, bool sortedPages, bool writeOnce)
        {
            var mergeCase = MergeGoldenCases.Build(name, sortedPages);
            var logger = new RecordingLogger<MergeStreamingGoldenTests>();
            var storage = new InMemoryBlobStorageRepository { MergeLogger = logger };
            foreach (var upload in mergeCase.Uploads)
            {
                storage.PutBytes(MergeCase.Prefix + upload.Name, upload.Bytes);
            }

            var count = writeOnce
                ? (await storage.MergeAndStreamUserBlobsWriteOnceAsync(
                    MergeCase.Prefix, MergeCase.OutputPath, mergeCase.Destination, mergeCase.RunId, mergeCase.SyncJobId,
                    mergeCase.Exclusionary, mergeCase.DryRun, mergeCase.Query)).Count
                : await storage.MergeAndStreamUserBlobsAsync(
                    MergeCase.Prefix, MergeCase.OutputPath, mergeCase.Destination, mergeCase.RunId, mergeCase.SyncJobId,
                    mergeCase.Exclusionary, mergeCase.DryRun, mergeCase.Query);

            AssertGolden(mergeCase, count, storage.GetBytes(MergeCase.OutputPath));
            AssertPath(mergeCase, logger);
        }

        /// <summary>
        /// Checks the captured golden and, for a readable failure, an independent oracle: the materializing
        /// algorithm (first occurrence in listing order wins, canonical sort, one <c>JsonSerializer.Serialize</c>).
        /// </summary>
        internal static void AssertGolden(MergeCase mergeCase, int count, byte[] actual)
        {
            Assert.IsNotNull(actual, $"{mergeCase.Name}: the merged membership was not written.");
            var oracle = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Oracle(mergeCase)));
            var golden = _goldens[mergeCase.Name];
            Assert.AreEqual(golden.Length, oracle.Length, $"{mergeCase.Name}: the oracle no longer reproduces the golden length.");
            Assert.AreEqual(golden.Sha256, Sha256(oracle), $"{mergeCase.Name}: the oracle no longer reproduces the golden bytes.");

            if (!actual.AsSpan().SequenceEqual(oracle))
            {
                var at = actual.AsSpan().CommonPrefixLength(oracle);
                Assert.Fail(
                    $"{mergeCase.Name}: output differs from the golden bytes at offset {at} (actual length {actual.Length}, golden {oracle.Length}).\n" +
                    $"actual: ...{Excerpt(actual, at)}...\ngolden: ...{Excerpt(oracle, at)}...");
            }

            Assert.AreEqual(golden.Sha256, Sha256(actual), $"{mergeCase.Name}: SHA-256 differs from the golden.");
            Assert.AreEqual(golden.Count, count, $"{mergeCase.Name}: returned member count.");
        }

        /// <summary>
        /// The streaming path ran (no warning) exactly when every upload is canonically ordered; otherwise the merge
        /// fell back to the materializing path and logged one warning.
        /// </summary>
        internal static void AssertPath<T>(MergeCase mergeCase, RecordingLogger<T> logger)
        {
            var warnings = logger.Entries.Where(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning).ToList();
            if (AllUploadsSorted(mergeCase))
            {
                Assert.AreEqual(0, warnings.Count, $"{mergeCase.Name}: sorted uploads must stream without a fallback warning.");
            }
            else
            {
                Assert.AreEqual(1, warnings.Count, $"{mergeCase.Name}: unsorted uploads must log one fallback warning.");
                StringAssert.Contains(warnings[0].Message, "not all in canonical order");
                StringAssert.Contains(warnings[0].Message, MergeCase.Prefix);
            }
        }

        internal static bool AllUploadsSorted(MergeCase mergeCase)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            foreach (var upload in mergeCase.Uploads)
            {
                var ids = JsonSerializer.Deserialize<List<AzureADUser>>(upload.Bytes, options)
                    .Where(u => u != null)
                    .Select(u => u.ObjectId)
                    .ToList();
                for (var i = 1; i < ids.Count; i++)
                {
                    if (ids[i].CompareTo(ids[i - 1]) < 0)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        internal static GroupMembership Oracle(MergeCase mergeCase)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var seen = new HashSet<Guid>();
            var members = new List<AzureADUser>();
            foreach (var upload in mergeCase.Uploads.OrderBy(u => u.Name, StringComparer.Ordinal))
            {
                var bytes = upload.Bytes;
                foreach (var user in JsonSerializer.Deserialize<List<AzureADUser>>(bytes, options).Where(u => u != null))
                {
                    if (seen.Add(user.ObjectId))
                    {
                        members.Add(user);
                    }
                }
            }

            members.Sort(CanonicalMemberComparer<AzureADUser>.Instance);
            return new GroupMembership
            {
                SourceMembers = members,
                Destination = mergeCase.Destination,
                RunId = mergeCase.RunId,
                SyncJobId = mergeCase.SyncJobId,
                Exclusionary = mergeCase.Exclusionary,
                MembershipObtainerDryRunEnabled = mergeCase.DryRun,
                Query = mergeCase.Query,
            };
        }

        internal static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

        private static string Excerpt(byte[] bytes, int at)
        {
            var start = Math.Max(0, at - 80);
            var length = Math.Min(bytes.Length - start, 160);
            return Encoding.UTF8.GetString(bytes, start, Math.Max(0, length));
        }
    }
}
