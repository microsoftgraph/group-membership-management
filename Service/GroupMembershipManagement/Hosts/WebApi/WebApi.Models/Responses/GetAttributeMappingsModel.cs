// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace WebApi.Models.Responses
{
    public class GetAttributeMappingsModel
    {
        public GetAttributeMappingsModel()
        {

        }

        public List<DTOs.SqlMembershipAttributeMapping> Mappings { get; set; } = new List<DTOs.SqlMembershipAttributeMapping>();

        /// <summary>
        /// True when more mappings matched than were returned, so the caller should search the server
        /// instead of filtering the returned page locally.
        /// </summary>
        public bool HasMore { get; set; }
    }
}