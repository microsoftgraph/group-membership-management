// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Models.Helpers;
using Models.ServiceBus;
using Moq;
using Repositories.BlobStorage;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Services.Tests
{
    internal sealed class MembershipMergeTestStore
    {
        private readonly Dictionary<string, byte[]> _membershipBlobs = new Dictionary<string, byte[]>();
        private readonly Dictionary<string, string> _textBlobs = new Dictionary<string, string>();
        private readonly Dictionary<string, int> _readAttempts = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _writeAttempts = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _manifestAttempts = new Dictionary<string, int>();
        private int _activeReaders;

        public MembershipMergeTestStore()
        {
            Repository = new Mock<IBlobStorageRepository>();
            Repository
                .Setup(repository => repository.StreamMembershipAsync(
                    It.IsAny<string>(),
                    It.IsAny<Action<GroupMembership>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((
                    string path,
                    Action<GroupMembership> onDetails,
                    CancellationToken cancellationToken) =>
                    ReadMembershipAsync(path, onDetails, cancellationToken));
            Repository
                .Setup(repository => repository.WriteMembershipAsync(
                    It.IsAny<string>(),
                    It.IsAny<GroupMembership>(),
                    It.IsAny<IAsyncEnumerable<AzureADUser>>(),
                    It.IsAny<Dictionary<string, string>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((
                    string path,
                    GroupMembership envelope,
                    IAsyncEnumerable<AzureADUser> members,
                    Dictionary<string, string> metadata,
                    CancellationToken cancellationToken) =>
                    WriteMembershipAsync(path, envelope, members, cancellationToken));
            Repository
                .Setup(repository => repository.UploadFileAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>()))
                .Returns((
                    string path,
                    string content,
                    Dictionary<string, string> metadata) =>
                {
                    _textBlobs[path] = content;
                    Operations.Add($"manifest:{path}");
                    return Task.CompletedTask;
                });
            Repository
                .Setup(repository => repository.UploadFileIfAbsentAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Returns((
                    string path,
                    string content,
                    CancellationToken cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var attempt = _manifestAttempts.TryGetValue(path, out var currentAttempt)
                        ? currentAttempt + 1
                        : 1;
                    _manifestAttempts[path] = attempt;
                    var failure = ManifestFailure?.Invoke(path, attempt);
                    if (failure != null)
                    {
                        throw failure;
                    }

                    if (_textBlobs.ContainsKey(path))
                    {
                        return Task.FromResult(false);
                    }

                    _textBlobs[path] = content;
                    Operations.Add($"manifest:{path}");
                    var failureAfterCommit = ManifestFailureAfterCommit?.Invoke(path, attempt);
                    if (failureAfterCommit != null)
                    {
                        throw failureAfterCommit;
                    }

                    return Task.FromResult(true);
                });
            Repository
                .Setup(repository => repository.DownloadFileAsync(It.IsAny<string>()))
                .Returns((string path) =>
                {
                    if (_textBlobs.TryGetValue(path, out var content))
                    {
                        return Task.FromResult(new BlobResult
                        {
                            BlobStatus = BlobStatus.Found,
                            Content = content,
                            Path = path
                        });
                    }

                    return Task.FromResult(new BlobResult { BlobStatus = BlobStatus.NotFound });
                });
            Repository
                .Setup(repository => repository.DeleteFileAsync(It.IsAny<string>()))
                .Returns((string path) =>
                {
                    _membershipBlobs.Remove(path);
                    _textBlobs.Remove(path);
                    Operations.Add($"delete:{path}");
                    return Task.CompletedTask;
                });
            Repository
                .Setup(repository => repository.DeleteFilesByPrefixAsync(
                    It.IsAny<string>(),
                    It.IsAny<bool>()))
                .Returns((string prefix, bool excludeLatest) =>
                {
                    foreach (var path in _membershipBlobs.Keys
                        .Where(path => path.StartsWith(prefix, StringComparison.Ordinal))
                        .ToArray())
                    {
                        _membershipBlobs.Remove(path);
                    }

                    foreach (var path in _textBlobs.Keys
                        .Where(path => path.StartsWith(prefix, StringComparison.Ordinal))
                        .ToArray())
                    {
                        _textBlobs.Remove(path);
                    }

                    Operations.Add($"delete-prefix:{prefix}");
                    return Task.CompletedTask;
                });
        }

        public Mock<IBlobStorageRepository> Repository { get; }

        public List<string> Operations { get; } = new List<string>();

        public IReadOnlyCollection<string> MembershipPaths => _membershipBlobs.Keys.ToArray();

        public long TotalMembershipBytes =>
            _membershipBlobs.Values.Sum(content => (long)content.Length);

        public int MaxActiveReaders { get; private set; }

        public Func<string, int, Exception> WriteFailure { get; set; }

        public Func<string, int, int, Exception> ReadFailure { get; set; }

        public Func<string, int, Exception> ManifestFailure { get; set; }

        public Func<string, int, Exception> ManifestFailureAfterCommit { get; set; }

        public Action<string> MembershipWriteCompleted { get; set; }

        public void AddRaw(string path, GroupMembership envelope, params AzureADUser[] members)
        {
            var content = CopyEnvelope(envelope);
            content.SourceMembers = new List<AzureADUser>(members);
            _membershipBlobs[path] = JsonSerializer.SerializeToUtf8Bytes(
                content,
                new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
                });
        }

        public void AddRawContent(string path, string content)
        {
            _membershipBlobs[path] = Encoding.UTF8.GetBytes(content);
        }

        public async Task AddCompressedAsync(
            string path,
            GroupMembership envelope,
            params AzureADUser[] members)
        {
            using var destination = new MemoryStream();
            await MembershipStream.WriteAsync(destination, envelope, Enumerate(members));
            _membershipBlobs[path] = destination.ToArray();
        }

        public bool ContainsText(string path) => _textBlobs.ContainsKey(path);

        public bool ContainsMembership(string path) => _membershipBlobs.ContainsKey(path);

        public string ReadText(string path) => _textBlobs[path];

        public byte[] ReadBytes(string path) => (byte[])_membershipBlobs[path].Clone();

        public int GetMembershipLength(string path) => _membershipBlobs[path].Length;

        public async Task<List<AzureADUser>> ReadMembersAsync(string path)
        {
            var members = new List<AzureADUser>();
            await foreach (var member in ReadMembershipAsync(path, null, CancellationToken.None))
            {
                members.Add(member);
            }

            return members;
        }

        public async Task<GroupMembership> ReadEnvelopeAsync(string path)
        {
            GroupMembership envelope = null;
            await foreach (var _ in ReadMembershipAsync(
                path,
                details => envelope = details,
                CancellationToken.None))
            {
            }

            return envelope;
        }

        private async IAsyncEnumerable<AzureADUser> ReadMembershipAsync(
            string path,
            Action<GroupMembership> onDetails,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (!_membershipBlobs.TryGetValue(path, out var content))
            {
                throw new FileNotFoundException($"Membership blob '{path}' was not found.", path);
            }

            var attempt = _readAttempts.TryGetValue(path, out var currentAttempt)
                ? currentAttempt + 1
                : 1;
            _readAttempts[path] = attempt;
            var memberIndex = 0;
            var activeReaders = Interlocked.Increment(ref _activeReaders);
            MaxActiveReaders = Math.Max(MaxActiveReaders, activeReaders);

            try
            {
                using var source = new MemoryStream(content);
                await foreach (var member in MembershipStream.ReadAsync(
                    source,
                    onDetails,
                    cancellationToken))
                {
                    var failure = ReadFailure?.Invoke(path, attempt, memberIndex);
                    if (failure != null)
                    {
                        throw failure;
                    }

                    yield return member;
                    memberIndex++;
                }
            }
            finally
            {
                Interlocked.Decrement(ref _activeReaders);
            }
        }

        private async Task WriteMembershipAsync(
            string path,
            GroupMembership envelope,
            IAsyncEnumerable<AzureADUser> members,
            CancellationToken cancellationToken)
        {
            var attempt = _writeAttempts.TryGetValue(path, out var currentAttempt)
                ? currentAttempt + 1
                : 1;
            _writeAttempts[path] = attempt;
            var failure = WriteFailure?.Invoke(path, attempt);
            if (failure != null)
            {
                throw failure;
            }

            using var destination = new MemoryStream();
            await MembershipStream.WriteAsync(destination, envelope, members, cancellationToken);
            _membershipBlobs[path] = destination.ToArray();
            Operations.Add($"write:{path}");
            MembershipWriteCompleted?.Invoke(path);
        }

        private static async IAsyncEnumerable<AzureADUser> Enumerate(
            IReadOnlyList<AzureADUser> members)
        {
            foreach (var member in members)
            {
                yield return member;
            }

            await Task.CompletedTask;
        }

        private static GroupMembership CopyEnvelope(GroupMembership envelope)
        {
            var json = JsonSerializer.Serialize(envelope);
            return JsonSerializer.Deserialize<GroupMembership>(json);
        }
    }
}
