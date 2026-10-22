// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Models.Helpers;
using Repositories.BlobStorage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Tests.Services.Infrastructure;

namespace Tests.Services.MergeStreaming
{
    /// <summary>
    /// The page-upload merge must use memory independent of the member count: O(inputs x small buffer). The
    /// production <see cref="BlobStorageRepository"/> runs over <see cref="CountingBlobSink"/>, which holds the
    /// inputs once and keeps no output, and a sampler forces full collections during the merge to measure the
    /// live managed heap above the pre-merge baseline (which already contains the inputs).
    /// </summary>
    [TestClass]
    public class MergeStreamingMemoryTests
    {
        private const long LiveHeapBudget = 64L * 1024 * 1024;
        private const int PageSize = 5_000;

        private static readonly Guid _sourceGroup = Guid.Parse("66666666-7777-8888-9999-aaaaaaaaaaaa");

        public TestContext TestContext { get; set; }

        [DataTestMethod]
        [DataRow(100_000, false)]
        [DataRow(100_000, true)]
        [DataRow(700_000, false)]
        [DataRow(700_000, true)]
        public async Task Merge_LiveHeapAboveInputs_StaysUnderBudget(int memberCount, bool writeOnce)
        {
            var sink = new CountingBlobSink();
            var expectedUnique = SeedSortedPages(sink, memberCount);
            var repository = new BlobStorageRepository(sink.Container.Object);
            var destination = new AzureADGroup { ObjectId = Guid.Parse(MergeCase.Prefix.Substring(0, 36)) };

            var baseline = SettledHeap();
            var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            using var sampler = new LiveHeapSampler();
            int count;
            if (writeOnce)
            {
                var result = await repository.MergeAndStreamUserBlobsWriteOnceAsync(
                    MergeCase.Prefix, MergeCase.OutputPath, destination, Guid.NewGuid(), Guid.NewGuid(), false, false, "[]");
                Assert.IsFalse(result.AlreadyExisted);
                count = result.Count;
            }
            else
            {
                count = await repository.MergeAndStreamUserBlobsAsync(
                    MergeCase.Prefix, MergeCase.OutputPath, destination, Guid.NewGuid(), Guid.NewGuid(), false, false, "[]");
            }

            var peak = sampler.Stop();
            var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            var peakAboveBaseline = Math.Max(0, peak - baseline);
            var peakAboveInputs = Math.Max(0, peak - sink.InputBytes);
            TestContext.WriteLine(
                $"N={memberCount} writeOnce={writeOnce} inputs={sink.InputBytes / 1048576.0:F1} MiB baseline={baseline / 1048576.0:F1} MiB " +
                $"peakLive={peak / 1048576.0:F1} MiB peakAboveBaseline={peakAboveBaseline / 1048576.0:F1} MiB peakAboveInputs={peakAboveInputs / 1048576.0:F1} MiB " +
                $"allocated={allocated / 1048576.0:F1} MiB samples={sampler.Samples} output={sink.GetOutput(MergeCase.OutputPath)?.Length / 1048576.0:F1} MiB");

            Assert.AreEqual(expectedUnique, count);
            Assert.IsNotNull(sink.GetOutput(MergeCase.OutputPath), "The merged membership was not written.");
            Assert.IsTrue(sampler.Samples > 0, "The sampler took no samples.");
            Assert.IsTrue(
                peakAboveBaseline < LiveHeapBudget,
                $"The merge of {memberCount:N0} members held {peakAboveBaseline / 1048576.0:F1} MiB of live heap above its inputs; the budget is {LiveHeapBudget / 1048576} MiB.");
        }

        /// <summary>Writes canonically sorted ~5,000-member page uploads, with about 1% duplicated into another page.</summary>
        private static int SeedSortedPages(CountingBlobSink sink, int memberCount)
        {
            var rng = new Random(16751398 + memberCount);
            var ids = new Guid[memberCount];
            var bytes = new byte[16];
            for (var i = 0; i < memberCount; i++)
            {
                rng.NextBytes(bytes);
                ids[i] = new Guid(bytes);
            }

            var pages = new List<List<AzureADUser>>();
            for (var start = 0; start < memberCount; start += PageSize)
            {
                pages.Add(ids.Skip(start).Take(PageSize).Select(id => new AzureADUser { ObjectId = id, SourceGroup = _sourceGroup }).ToList());
            }

            for (var d = 0; d < memberCount / 100; d++)
            {
                var j = rng.Next(memberCount);
                var page = (j / PageSize + 1 + rng.Next(Math.Max(1, pages.Count - 1))) % pages.Count;
                pages[page].Add(new AzureADUser { ObjectId = ids[j], SourceGroup = _sourceGroup });
            }

            var readerOptions = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault };
            for (var p = 0; p < pages.Count; p++)
            {
                var sorted = pages[p].OrderBy(user => user, CanonicalMemberComparer<AzureADUser>.Instance).ToList();
                sink.PutInput(MergeCase.Prefix + $"{p:D8}-0000-0000-0000-000000000000.json", JsonSerializer.SerializeToUtf8Bytes(sorted, readerOptions));
            }

            return ids.Distinct().Count();
        }

        private static long SettledHeap()
        {
            for (var i = 0; i < 3; i++)
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
            }

            return GC.GetTotalMemory(forceFullCollection: true);
        }

        /// <summary>Forces a full blocking collection every few milliseconds and records the largest live heap.</summary>
        private sealed class LiveHeapSampler : IDisposable
        {
            private readonly Thread _thread;
            private volatile bool _stop;
            private long _peak;

            public LiveHeapSampler()
            {
                _thread = new Thread(() =>
                {
                    while (!_stop)
                    {
                        Sample();
                        Thread.Sleep(10);
                    }
                })
                { IsBackground = true, Priority = ThreadPriority.AboveNormal };
                _thread.Start();
            }

            public int Samples { get; private set; }

            public long Stop()
            {
                if (!_stop)
                {
                    _stop = true;
                    _thread.Join();
                }

                return Interlocked.Read(ref _peak);
            }

            public void Dispose() => Stop();

            private void Sample()
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true);
                var live = GC.GetTotalMemory(forceFullCollection: false);
                if (live > _peak)
                {
                    Interlocked.Exchange(ref _peak, live);
                }

                Samples++;
            }
        }
    }
}
