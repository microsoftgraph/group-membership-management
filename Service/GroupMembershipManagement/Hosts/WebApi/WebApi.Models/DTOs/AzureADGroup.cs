// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace WebApi.Models.DTOs
{
    public class AzureADGroup
    {
        public AzureADGroup(
            Guid id,
            //string alias,
            string name,
            List<string> endpoints)
        {
            Id = id;
            //Alias = alias;
            Name = name;
            Endpoints = endpoints;
        }

        public Guid Id { get; set; }
        //public string Alias { get; set; }
        public string Name { get; set; }
        public List<string> Endpoints { get; set; }
    }
}
