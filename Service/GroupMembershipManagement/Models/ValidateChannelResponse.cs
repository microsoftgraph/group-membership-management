// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models.Entities;

namespace Models
{
    public class ValidateChannelResponse
    {
        public AzureADTeamsChannel ParsedChannel { get; set; }
        public bool IsValid { get; set; }
    }
}
