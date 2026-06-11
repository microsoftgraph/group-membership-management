// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;

namespace Models
{
    [ExcludeFromCodeCoverage]
    public class GroupMembershipFileResult
    {
        public string FilePath { get; set; }
        public int MemberCount { get; set; }
    }
}