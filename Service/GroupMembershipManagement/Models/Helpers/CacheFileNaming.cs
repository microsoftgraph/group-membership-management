// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Models.Helpers
{
    public static class CacheFileNaming
    {
        public static string BuildCacheFileName(Guid groupId, DateTime utcNow) => $"cache/{groupId}_{utcNow:MMddyyyy-HHmm}.txt";

        public static string BuildCacheFileNamePrefix(Guid groupId) => $"cache/{groupId}";
    }
}
