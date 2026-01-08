// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Hosts.MessageSplitter
{
    public record MarkCapacityDeniedRequest(long SequenceNumber, DateTimeOffset DeniedAtUtc);
}
