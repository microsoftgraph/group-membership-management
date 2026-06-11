// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Models
{
    [ExcludeFromCodeCoverage]
    public class GraphProfileInformation: IEquatable<GraphProfileInformation>
    {
        /// <summary>
        /// Gets or sets the AAD Object Id of a Graph User
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }
        /// <summary>
        /// Gets or sets the onPremisesImmutableId of a Graph User
        /// </summary>
        [JsonPropertyName("onPremisesImmutableId")]
        public string PersonnelNumber { get; set; }
        /// <summary>
        /// Gets or sets the userPrincipalName of a Graph User
        /// </summary>
        [JsonPropertyName("userPrincipalName")]
        public string UserPrincipalName { get; set; }

        public bool Equals(GraphProfileInformation other)
        {
            return PersonnelNumber.Equals(other.PersonnelNumber);
        }

        public override int GetHashCode()
        {
            return PersonnelNumber.GetHashCode();
        }
    }
}
