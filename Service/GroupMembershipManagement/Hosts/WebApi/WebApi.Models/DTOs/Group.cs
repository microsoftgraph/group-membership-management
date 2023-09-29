// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace WebApi.Models.DTOs
{
    public class Group
    {
        public Group(
            Guid id,
            //string alias,
            string name)
        {
            Id = id;
            //Alias = alias;
            Name = name;
        }

        public Guid Id { get; set; }
        //public string Alias { get; set; }
        public string Name { get; set; }
    }
}
