// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Services.Tests
{
    internal sealed class TestAsyncDisposable : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
