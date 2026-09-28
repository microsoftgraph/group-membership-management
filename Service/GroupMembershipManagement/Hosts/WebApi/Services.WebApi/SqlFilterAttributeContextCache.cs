// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Services
{
    /// <summary>
    /// Process-wide cache for the tenant-level lookups that AI title generation needs.
    /// <para>
    /// Title generation is re-triggered on a debounce while a group owner edits a query, so the
    /// ADF run id lookup (an ARM control-plane call that enumerates a month of pipeline runs) and
    /// the admin-configured attribute labels would otherwise be re-fetched many times per minute
    /// per editing user. Both change at most once per pipeline run or admin edit, so they are
    /// memoized here. Mirrors the semaphore + TTL cache SqlMembershipObtainer's DataFactoryService
    /// applies to the same ADF call.
    /// </para>
    /// <para>Registered as a singleton; the owning service is scoped, so the cache cannot live on it.</para>
    /// </summary>
    public sealed class SqlFilterAttributeContextCache
    {
        /// <summary>How long a successfully resolved value is reused.</summary>
        public static readonly TimeSpan ResolvedDuration = TimeSpan.FromMinutes(30);

        /// <summary>
        /// How long an unresolved value is reused. Kept short so a transient failure degrades titles
        /// only briefly, but long enough that a persistent one cannot be retried on every request.
        /// </summary>
        public static readonly TimeSpan UnresolvedDuration = TimeSpan.FromMinutes(1);

        private readonly CacheSlot<string?> _mappingsTableName = new();
        private readonly CacheSlot<IReadOnlyDictionary<string, string>> _attributeLabels = new();

        public Task<string?> GetMappingsTableNameAsync(Func<Task<(string? Value, TimeSpan Duration)>> resolve)
        {
            return _mappingsTableName.GetAsync(resolve);
        }

        public Task<IReadOnlyDictionary<string, string>> GetAttributeLabelsAsync(
            Func<Task<(IReadOnlyDictionary<string, string> Value, TimeSpan Duration)>> resolve)
        {
            return _attributeLabels.GetAsync(resolve);
        }

        private sealed class CacheSlot<T>
        {
            private readonly SemaphoreSlim _semaphore = new(1, 1);

            // A single immutable reference keeps the value and its expiry consistent for the
            // lock-free read below, which would otherwise be able to observe one without the other.
            private Entry? _entry;

            public async Task<T> GetAsync(Func<Task<(T Value, TimeSpan Duration)>> resolve)
            {
                if (TryRead(out var cached))
                {
                    return cached;
                }

                await _semaphore.WaitAsync();
                try
                {
                    // A concurrent caller may have refreshed the entry while this one waited.
                    if (TryRead(out cached))
                    {
                        return cached;
                    }

                    var (value, duration) = await resolve();
                    Volatile.Write(ref _entry, new Entry(value, DateTime.UtcNow.Add(duration)));
                    return value;
                }
                finally
                {
                    _semaphore.Release();
                }
            }

            private bool TryRead(out T value)
            {
                var entry = Volatile.Read(ref _entry);
                if (entry != null && DateTime.UtcNow < entry.ExpiresAt)
                {
                    value = entry.Value;
                    return true;
                }

                value = default!;
                return false;
            }

            private sealed record Entry(T Value, DateTime ExpiresAt);
        }
    }
}
