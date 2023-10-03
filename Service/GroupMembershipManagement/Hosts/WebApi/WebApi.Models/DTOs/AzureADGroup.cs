// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace WebApi.Models.DTOs
{
    public class AzureADGroup
    {
        public AzureADGroup(Guid objectId, string name)
        {
            Id = objectId;
            Name = name;
        }

        public Guid Id { get; set; }
        public string Name { get; set; }
    }
}
