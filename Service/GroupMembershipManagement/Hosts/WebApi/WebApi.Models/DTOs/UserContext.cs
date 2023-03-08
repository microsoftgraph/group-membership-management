// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace WebApi.Models.DTOs
{
    public record UserContext
    {
        public string UserId { get; private set; }
        public string Alias { get; private set; }

        public UserContext(string userId, string alias)
        {
            UserId = userId;
            Alias = alias;
        }
    }
}
