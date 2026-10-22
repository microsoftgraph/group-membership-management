// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Models;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Services.Tests.Mocks
{
    public class MockBlobStorageRepository : IBlobStorageRepository
    {
        public Dictionary<string, string> Files { get; set; } = new Dictionary<string, string>();
        private readonly Dictionary<string, long> _writeSequences =
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private long _writeSequence;

        public Task DeleteFileAsync(string path)
        {
            throw new NotImplementedException();
        }

        public Task<BlobResult> DownloadFileAsync(string path, System.Threading.CancellationToken cancellationToken) =>
            DownloadFileAsync(path);

        public Task<BlobResult> DownloadFileAsync(string path)
        {
            var result = new BlobResult();

            if (Files.ContainsKey(path))
            {
                result.BlobStatus = BlobStatus.Found;
                result.Content = Files[path];
            }
            else
            {
                result.BlobStatus = BlobStatus.NotFound;
            }

            return Task.FromResult(result);
        }

        public Task<BlobResult> DownloadCacheFileAsync(string path)
        {
            throw new NotImplementedException();
        }

        public Task DeleteFilesAsync(string path)
        {
            throw new NotImplementedException();
        }

        public Task<BlobMetadataResult> GetBlobMetadataAsync(string path)
        {
            throw new NotImplementedException();
        }

        public Task UploadFileAsync(string path, string content, Dictionary<string, string> metadata = null)
        {
            Files[path] = content;
            _writeSequences[path] = ++_writeSequence;
            return Task.CompletedTask;
        }

        public Task UploadFileAsync(
            string path,
            string content,
            Dictionary<string, string> metadata,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return UploadFileAsync(path, content, metadata);
        }

        public Task<bool> UploadFileIfAbsentAsync(
            string path,
            string content,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Files.ContainsKey(path))
            {
                return Task.FromResult(false);
            }

            Files[path] = content;
            return Task.FromResult(true);
        }

        public Task UploadFileStreamAsync<T>(string path, T content, Dictionary<string, string> metadata = null, JsonSerializerOptions serializerOptions = null)
        {
            throw new NotImplementedException();
        }

        public Task UploadGroupMembershipFromGuidsAsync(
            string membershipFilePath,
            IReadOnlyCollection<Guid> sourceMemberIds,
            AzureADGroup destination,
            Guid runId,
            Guid syncJobId,
            bool exclusionary,
            bool membershipObtainerDryRunEnabled,
            string query)
        {
            throw new NotImplementedException();
        }

        public Task UploadGroupMembershipFromGuidsAsync(
            string membershipFilePath,
            IReadOnlyCollection<Guid> sourceMemberIds,
            AzureADGroup destination,
            Guid runId,
            Guid syncJobId,
            bool exclusionary,
            bool membershipObtainerDryRunEnabled,
            string query,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return UploadGroupMembershipFromGuidsAsync(
                membershipFilePath,
                sourceMemberIds,
                destination,
                runId,
                syncJobId,
                exclusionary,
                membershipObtainerDryRunEnabled,
                query);
        }

        public Task UploadGroupMembershipFromGuidsAsync(
            string filePath,
            IReadOnlyCollection<Guid> sourceMemberIds,
            AzureADGroup destination,
            Guid runId,
            Guid syncJobId,
            bool exclusionary,
            bool membershipObtainerDryRunEnabled,
            string query,
            Guid sourceGroupId,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<WriteOnceResult> UploadGroupMembershipFromGuidsWriteOnceAsync(
            string filePath,
            IReadOnlyCollection<Guid> sourceMemberIds,
            AzureADGroup destination,
            Guid runId,
            Guid syncJobId,
            bool exclusionary,
            bool membershipObtainerDryRunEnabled,
            string query,
            Guid sourceGroupId,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task UploadCacheFromGuidsAsync(
            string destinationCacheFilePath,
            IEnumerable<Guid> sourceMemberIds,
            Dictionary<string, string> metadata = null)
        {
            var ids = sourceMemberIds
                .Distinct()
                .OrderBy(id => id)
                .ToList();
            Files[destinationCacheFilePath] =
                string.Join(Environment.NewLine, ids) +
                (ids.Count == 0 ? string.Empty : Environment.NewLine);
            _writeSequences[destinationCacheFilePath] = ++_writeSequence;
            return Task.CompletedTask;
        }

        public Task<int> MergeAndStreamUserBlobsAsync(
            string sourceBlobPrefix,
            string destinationPath,
            AzureADGroup destination,
            Guid runId,
            Guid syncJobId,
            bool exclusionary,
            bool membershipObtainerDryRunEnabled,
            string query)
        {
            throw new NotImplementedException();
        }

        public IAsyncEnumerable<AzureADUser> StreamMembershipAsync(
            string path,
            Action<Models.ServiceBus.GroupMembership> onMembershipDetailsKnown = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task WriteMembershipAsync(
            string path,
            Models.ServiceBus.GroupMembership envelope,
            IAsyncEnumerable<AzureADUser> members,
            Dictionary<string, string> metadata = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<string> UploadFileBlockAsync(string path, string content, Dictionary<string, string> metadata = null)
        {
            throw new NotImplementedException();
        }

        public Task CommitFileAsync(string path, List<string> blockIds)
        {
            throw new NotImplementedException();
        }

        public Task DeleteFilesByPrefixAsync(string prefix, bool excludeLatest = false)
        {
            var matches = Files.Keys
                .Where(path => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(path =>
                    _writeSequences.TryGetValue(path, out var sequence)
                        ? sequence
                        : 0)
                .ToList();
            if (excludeLatest && matches.Count > 0)
            {
                matches.RemoveAt(0);
            }

            foreach (var path in matches)
            {
                Files.Remove(path);
                _writeSequences.Remove(path);
            }

            return Task.CompletedTask;
        }

        public Task<List<AzureADUser>> ReadBlobsAsync(string path) => Task.FromResult(new List<AzureADUser>());

        public async IAsyncEnumerable<AzureADUser> StreamUsersFromBlobsAsync(string path)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task DeleteBlobsAsync(string path)
        {
            throw new NotImplementedException();
        }

        public Task<BlobResult> FindLatestFileAsync(string prefix)
        {
            throw new NotImplementedException();
        }

        public Task<BlobResult> FindAggregatedFileByRunIdAsync(string groupId, string runId)
        {
            throw new NotImplementedException();
        }

        public Task<Dictionary<string, BlobResult>> FindPartFilesByRunIdAsync(string groupId, string runId)
        {
            throw new NotImplementedException();
        }

        public Task<HashSet<T>> ReadValuesFromBlobAsync<T>(string path, Func<string, T> parseFunction)
        {
            if (!Files.TryGetValue(path, out var content))
            {
                return Task.FromResult(new HashSet<T>());
            }

            var values = new HashSet<T>();
            using var reader = new StringReader(content);
            while (reader.ReadLine() is { } line)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    values.Add(parseFunction(line));
                }
            }

            return Task.FromResult(values);
        }

        public Task<HashSet<T>> ReadValuesFromBlobAsync<T>(
            string path,
            Func<string, T> parseFunction,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ReadValuesFromBlobAsync(path, parseFunction);
        }

        public Task<HashSet<Guid>> ExtractGroupMembershipSourceMembersAsync(string path)
        {
            throw new NotImplementedException();
        }

        public Task<int> StreamMembershipToCacheAsync(string sourceMembershipFilePath, string destinationCacheFilePath, Dictionary<string, string> metadata = null)
        {
            // For testing, just return 0 - tests can override this behavior if needed
            return Task.FromResult(0);
        }

        // GraphUpdater does not use the write-once uploads; these exist only to satisfy the contract.
        public Task<WriteOnceResult> UploadFileWriteOnceAsync(
            string path,
            string content,
            Dictionary<string, string> metadata = null,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<WriteOnceResult> UploadGroupMembershipFromGuidsWriteOnceAsync(
            string membershipFilePath,
            IReadOnlyCollection<Guid> sourceMemberIds,
            AzureADGroup destination,
            Guid runId,
            Guid syncJobId,
            bool exclusionary,
            bool membershipObtainerDryRunEnabled,
            string query,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<WriteOnceResult> UploadCacheFromGuidsWriteOnceAsync(
            string destinationCacheFilePath,
            IEnumerable<Guid> sourceMemberIds,
            Dictionary<string, string> metadata = null) =>
            throw new NotImplementedException();

        public Task<WriteOnceResult> StreamMembershipToCacheWriteOnceAsync(
            string sourceMembershipFilePath,
            string destinationCacheFilePath,
            Dictionary<string, string> metadata = null) =>
            throw new NotImplementedException();

        public Task<WriteOnceResult> MergeAndStreamUserBlobsWriteOnceAsync(
            string sourceBlobPrefix,
            string destinationPath,
            AzureADGroup destination,
            Guid runId,
            Guid syncJobId,
            bool exclusionary,
            bool membershipObtainerDryRunEnabled,
            string query) =>
            throw new NotImplementedException();

        // Selection pipeline members (story 16751398, slice F). Not used by these tests.
        public Task<WriteOnceResult> UploadGroupMembershipStreamWriteOnceAsync(
            string path,
            Models.ServiceBus.GroupMembership envelope,
            IAsyncEnumerable<AzureADUser> members,
            Action beforeCommit,
            CancellationToken stagingCancellationToken,
            CancellationToken commitCancellationToken) =>
            throw new NotImplementedException();

        public Task<BlobStreamWithETag> OpenReadWithETagAsync(string path, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<bool> UploadFileConditionalStreamAsync(
            string path,
            Func<System.IO.Stream, CancellationToken, Task> writeContent,
            string ifMatchETag,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<int> DeleteListedBlobsAsync(
            IReadOnlyCollection<string> exactPaths,
            IReadOnlyCollection<string> prefixes,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}
