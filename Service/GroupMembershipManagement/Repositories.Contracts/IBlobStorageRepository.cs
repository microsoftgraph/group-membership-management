// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Models.ServiceBus;

namespace Repositories.Contracts
{
    public interface IBlobStorageRepository
    {
        public Task UploadFileAsync(string path, string content, Dictionary<string, string> metadata = null);

        /// <summary>Uploads text to the requested blob path and cancels the Azure operation when requested.</summary>
        /// <param name="path">The blob path to create or replace.</param>
        /// <param name="content">The text to upload. Null is handled by the repository implementation.</param>
        /// <param name="metadata">Optional blob metadata. Pass null or an empty dictionary for no metadata.</param>
        /// <param name="cancellationToken">Cancels the upload. The returned task throws on cancellation.</param>
        /// <returns>A task that completes after the blob and metadata are committed.</returns>
        public Task UploadFileAsync(
            string path,
            string content,
            Dictionary<string, string> metadata,
            CancellationToken cancellationToken);

        /// <summary>Uploads a blob only when the path does not exist and returns whether it was created.</summary>
        public Task<bool> UploadFileIfAbsentAsync(string path, string content, CancellationToken cancellationToken = default);
        Task<string> UploadFileBlockAsync(string path, string content, Dictionary<string, string> metadata = null);
        public Task DeleteFileAsync(string path);
        public Task DeleteFilesByPrefixAsync(string prefix, bool excludeLatest = false);
        public Task<BlobResult> DownloadFileAsync(string path);

        /// <summary>Downloads a blob's text and metadata, cancelling the existence check and the download when requested.</summary>
        /// <param name="path">The blob path to read.</param>
        /// <param name="cancellationToken">Cancels the requests. The returned task throws on cancellation.</param>
        /// <returns>The content and metadata, or <see cref="BlobStatus.NotFound"/> when the blob does not exist.</returns>
        public Task<BlobResult> DownloadFileAsync(string path, CancellationToken cancellationToken);
        public Task<BlobResult> DownloadCacheFileAsync(string path);
        public Task DeleteFilesAsync(string path);
        public Task<BlobMetadataResult> GetBlobMetadataAsync(string path);
        public Task CommitFileAsync(string path, List<string> blockIds);
        public Task<List<AzureADUser>> ReadBlobsAsync(string path);
        public IAsyncEnumerable<AzureADUser> StreamUsersFromBlobsAsync(string path);
        public Task DeleteBlobsAsync(string path);
        public Task<BlobResult> FindLatestFileAsync(string prefix);
        public Task<BlobResult> FindAggregatedFileByRunIdAsync(string groupId, string runId);

        /// <summary>
        /// Enumerates the destination group's blob container for all per-part membership files
        /// written during a specific sync run (e.g. {timestamp}_{runId}_GroupMembership_1.json,
        /// {timestamp}_{runId}_SqlMembership_3.json, etc.), keyed by the file's "{PartType}_{Index}"
        /// suffix so callers can look up a specific part directly. The Aggregated file is
        /// intentionally excluded — callers already have FindAggregatedFileByRunIdAsync for that.
        /// Returns an empty dictionary when no per-part files exist for the run (e.g. after retention
        /// trimmed them) so callers can degrade gracefully.
        /// </summary>
        public Task<Dictionary<string, BlobResult>> FindPartFilesByRunIdAsync(string groupId, string runId);

        public Task<HashSet<T>> ReadValuesFromBlobAsync<T>(string path, System.Func<string, T> parseFunction);

        /// <summary>Reads non-empty lines from a blob and parses each distinct value, with cancellation.</summary>
        /// <typeparam name="T">The parsed value type.</typeparam>
        /// <param name="path">The blob path to read. A missing blob returns an empty set.</param>
        /// <param name="parseFunction">Parses one non-empty line. Parser exceptions propagate.</param>
        /// <param name="cancellationToken">Cancels existence checks, streaming, and line reads.</param>
        /// <returns>The distinct parsed values, or an empty set when the blob does not exist.</returns>
        public Task<HashSet<T>> ReadValuesFromBlobAsync<T>(
            string path,
            System.Func<string, T> parseFunction,
            CancellationToken cancellationToken);

        /// <summary>
        /// Extract distinct SourceMembers[].ObjectId GUIDs from a GroupMembership JSON blob at the given path.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public Task<HashSet<Guid>> ExtractGroupMembershipSourceMembersAsync(string path);

        /// <summary>
        /// Stream GUIDs from a GroupMembership JSON blob and write them as a cache file (one GUID per line).
        /// This avoids loading all GUIDs into memory at once.
        /// </summary>
        /// <param name="sourceMembershipFilePath">Path to the source GroupMembership JSON blob.</param>
        /// <param name="destinationCacheFilePath">Path where the cache file should be written.</param>
        /// <param name="metadata">Optional metadata to attach to the cache blob.</param>
        /// <returns>The count of GUIDs written to the cache.</returns>
        public Task<int> StreamMembershipToCacheAsync(string sourceMembershipFilePath, string destinationCacheFilePath, Dictionary<string, string> metadata = null);

        /// <summary>
        /// Serialize an object directly to blob storage as JSON without creating intermediate string.
        /// This reduces memory usage for large objects by streaming the serialization.
        /// </summary>
        /// <typeparam name="T">Type of the object to serialize.</typeparam>
        /// <param name="path">Blob path.</param>
        /// <param name="content">Object to serialize.</param>
        /// <param name="metadata">Optional metadata to attach to the blob.</param>
        public Task UploadFileStreamAsync<T>(string path, T content, Dictionary<string, string> metadata = null, JsonSerializerOptions serializerOptions = null);

        public Task UploadGroupMembershipFromGuidsAsync(
            string membershipFilePath,
            IReadOnlyCollection<Guid> sourceMemberIds,
            AzureADGroup destination,
            Guid runId,
            Guid syncJobId,
            bool exclusionary,
            bool membershipObtainerDryRunEnabled,
            string query);

        /// <summary>Writes a group-membership document from object identifiers and supports cancellation.</summary>
        /// <param name="membershipFilePath">The destination membership blob path.</param>
        /// <param name="sourceMemberIds">The source member identifiers to write in canonical order.</param>
        /// <param name="destination">The destination group stored in the membership envelope.</param>
        /// <param name="runId">The current synchronization run identifier.</param>
        /// <param name="syncJobId">The synchronization job identifier.</param>
        /// <param name="exclusionary">Whether the source rule is exclusionary.</param>
        /// <param name="membershipObtainerDryRunEnabled">Whether the run is a membership-obtainer dry run.</param>
        /// <param name="query">The source job query, or null when no query is stored.</param>
        /// <param name="cancellationToken">Cancels the blob write and JSON flush.</param>
        /// <returns>A task that completes after the membership document is committed.</returns>
        public Task UploadGroupMembershipFromGuidsAsync(
            string membershipFilePath,
            IReadOnlyCollection<Guid> sourceMemberIds,
            AzureADGroup destination,
            Guid runId,
            Guid syncJobId,
            bool exclusionary,
            bool membershipObtainerDryRunEnabled,
            string query,
            CancellationToken cancellationToken);

        /// <summary>
        /// Writes the identifier-based group-membership document with <c>SourceGroup</c> on every member. Used only for
        /// a one-type part's selected saved-delta-link output (FR-010, I14); the both-types saved-delta file keeps the
        /// identifier-only overload. The bytes are the identifier-only bytes with <c>"SourceGroup":"..."</c> after each
        /// member's <c>ObjectId</c>.
        /// </summary>
        /// <param name="sourceGroupId">The source group recorded on every member; must not be <see cref="Guid.Empty"/>.</param>
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
            CancellationToken cancellationToken = default);

        public Task UploadCacheFromGuidsAsync(
            string destinationCacheFilePath,
            IEnumerable<Guid> sourceMemberIds,
            Dictionary<string, string> metadata = null);

        // Write-once uploads. Each one stages the whole content and publishes it in one final request
        // conditioned on If-None-Match: *, so a partial blob is never visible at the name. A conflict or
        // precondition rejection (409, 412, or InvalidBlockList from a concurrent commit) is reported as
        // AlreadyExisted only after confirming the blob exists; otherwise the failure is rethrown.
        // Content and metadata are byte-identical to the matching overwriting upload.

        /// <summary>Write-once form of <see cref="UploadFileAsync(string, string, Dictionary{string, string})"/>.</summary>
        public Task<WriteOnceResult> UploadFileWriteOnceAsync(
            string path,
            string content,
            Dictionary<string, string> metadata = null,
            CancellationToken cancellationToken = default);

        /// <summary>Write-once form of <c>UploadGroupMembershipFromGuidsAsync</c>. Count is the number of members written.</summary>
        public Task<WriteOnceResult> UploadGroupMembershipFromGuidsWriteOnceAsync(
            string membershipFilePath,
            IReadOnlyCollection<Guid> sourceMemberIds,
            AzureADGroup destination,
            Guid runId,
            Guid syncJobId,
            bool exclusionary,
            bool membershipObtainerDryRunEnabled,
            string query,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Write-once form of the source-group overload of <c>UploadGroupMembershipFromGuidsAsync</c>: writes
        /// <paramref name="sourceGroupId"/> as every member's <c>SourceGroup</c>, so a one-type saved-delta part keeps
        /// source attribution (FR-010, I14). Count is the number of members written.
        /// </summary>
        /// <param name="sourceGroupId">The source group recorded on every member; must not be <see cref="Guid.Empty"/>.</param>
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
            CancellationToken cancellationToken = default);

        /// <summary>Write-once form of <see cref="UploadCacheFromGuidsAsync"/>. Count is the number of identifiers written.</summary>
        public Task<WriteOnceResult> UploadCacheFromGuidsWriteOnceAsync(
            string destinationCacheFilePath,
            IEnumerable<Guid> sourceMemberIds,
            Dictionary<string, string> metadata = null);

        /// <summary>
        /// Write-once form of <see cref="StreamMembershipToCacheAsync"/>. Count is the number of identifiers
        /// read from the source membership blob.
        /// </summary>
        public Task<WriteOnceResult> StreamMembershipToCacheWriteOnceAsync(
            string sourceMembershipFilePath,
            string destinationCacheFilePath,
            Dictionary<string, string> metadata = null);

        /// <summary>
        /// Write-once form of <see cref="MergeAndStreamUserBlobsAsync"/>. Count is the number of unique members
        /// written, or, when the destination already existed, the number of unique members in the existing blob
        /// (never the count of the current, possibly already cleaned-up, source listing).
        /// </summary>
        public Task<WriteOnceResult> MergeAndStreamUserBlobsWriteOnceAsync(
            string sourceBlobPrefix,
            string destinationPath,
            AzureADGroup destination,
            Guid runId,
            Guid syncJobId,
            bool exclusionary,
            bool membershipObtainerDryRunEnabled,
            string query);

        /// <summary>
        /// Merges the reader page uploads (JSON arrays of users, each written in canonical order) into one
        /// GroupMembership JSON blob: a streaming k-way merge that keeps one current member per upload, drops
        /// duplicate ObjectIds (the first occurrence in listing order wins), writes members in canonical order into
        /// staged blocks and publishes them with one commit. Peak memory is O(uploads x small buffer), independent of
        /// the member count. The bytes equal <c>JsonSerializer.Serialize(groupMembership)</c> of the merged
        /// membership. An upload that is not in canonical order (written by an older release) makes the call fall
        /// back to an in-memory merge with the same result, and logs a warning.
        /// </summary>
        /// <param name="sourceBlobPrefix">Prefix to find source user array blobs.</param>
        /// <param name="destinationPath">Path for output GroupMembership JSON blob.</param>
        /// <param name="destination">The destination group.</param>
        /// <param name="runId">The run ID.</param>
        /// <param name="syncJobId">The sync job ID.</param>
        /// <param name="exclusionary">Whether this is an exclusionary sync.</param>
        /// <param name="membershipObtainerDryRunEnabled">Dry run flag.</param>
        /// <param name="query">The sync job query.</param>
        /// <returns>The count of unique users written.</returns>
        public Task<int> MergeAndStreamUserBlobsAsync(
            string sourceBlobPrefix,
            string destinationPath,
            AzureADGroup destination,
            Guid runId,
            Guid syncJobId,
            bool exclusionary,
            bool membershipObtainerDryRunEnabled,
            string query);

        // Group-part selection pipeline (story 16751398, slice F).

        /// <summary>
        /// Streams a plain-JSON membership document (the bytes <c>JsonSerializer.Serialize(GroupMembership)</c>
        /// produces) and publishes it write-once with one final conditional commit. <paramref name="beforeCommit"/>
        /// runs after every member is staged and before the commit; an exception it throws aborts the write and
        /// leaves no blob. <paramref name="stagingCancellationToken"/> cancels reading the members and staging blocks;
        /// <paramref name="commitCancellationToken"/> cancels only the final commit request. Count is the number of
        /// members written, or the number in the existing blob when it already existed.
        /// </summary>
        public Task<WriteOnceResult> UploadGroupMembershipStreamWriteOnceAsync(
            string path,
            GroupMembership envelope,
            IAsyncEnumerable<AzureADUser> members,
            Action beforeCommit,
            CancellationToken stagingCancellationToken,
            CancellationToken commitCancellationToken);

        /// <summary>
        /// Opens a blob for streaming with the ETag and last-modified time of the version read, in one call.
        /// Returns <see langword="null"/> when the blob does not exist. Dispose the result.
        /// </summary>
        public Task<BlobStreamWithETag> OpenReadWithETagAsync(string path, CancellationToken cancellationToken = default);

        /// <summary>
        /// Writes a blob with one conditional commit: <c>If-Match</c> on <paramref name="ifMatchETag"/>, or
        /// <c>If-None-Match: *</c> when it is null. Returns <see langword="false"/> when the condition fails
        /// (409, 412, or a concurrent commit's InvalidBlockList); other failures throw.
        /// </summary>
        public Task<bool> UploadFileConditionalStreamAsync(
            string path,
            Func<System.IO.Stream, CancellationToken, Task> writeContent,
            string ifMatchETag,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes, by listing, the blobs named exactly by <paramref name="exactPaths"/> and every blob under
        /// <paramref name="prefixes"/>. Only listed blobs are deleted, so absent blobs cause no 404 requests.
        /// Returns the number of blobs deleted.
        /// </summary>
        public Task<int> DeleteListedBlobsAsync(
            IReadOnlyCollection<string> exactPaths,
            IReadOnlyCollection<string> prefixes,
            CancellationToken cancellationToken = default);

        /// <summary>Reads a membership blob one member at a time.</summary>
        /// <param name="path">Blob path.</param>
        /// <param name="onMembershipDetailsKnown">
        /// Called once before the members when the envelope comes first, otherwise after the full read.
        /// SourceMembers is empty.
        /// </param>
        /// <param name="cancellationToken">Cancels the read.</param>
        public IAsyncEnumerable<AzureADUser> StreamMembershipAsync(string path, Action<GroupMembership> onMembershipDetailsKnown = null, CancellationToken cancellationToken = default);

        /// <summary>Writes a membership blob from a stream of members.</summary>
        /// <remarks>Publishes content and metadata only after serialization completes.</remarks>
        public Task WriteMembershipAsync(string path, GroupMembership envelope, IAsyncEnumerable<AzureADUser> members, Dictionary<string, string> metadata = null, CancellationToken cancellationToken = default);
    }
}
