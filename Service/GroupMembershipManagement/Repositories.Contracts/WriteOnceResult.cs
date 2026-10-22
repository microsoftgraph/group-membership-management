// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Repositories.Contracts
{
    /// <summary>
    /// The outcome of a write-once blob upload.
    /// </summary>
    /// <param name="AlreadyExisted">
    /// <see langword="true"/> when the blob already existed, so this write was rejected and the existing
    /// blob was kept; <see langword="false"/> when this call created the blob.
    /// </param>
    /// <param name="Count">
    /// The member count for uploads that report one: the count written when the blob was created, or the
    /// count read from the existing blob when the upload says so. Zero for uploads without a count.
    /// </param>
    public readonly record struct WriteOnceResult(bool AlreadyExisted, int Count = 0);
}
