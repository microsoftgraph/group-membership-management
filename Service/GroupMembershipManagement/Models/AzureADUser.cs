// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;



namespace Models
{
    [ExcludeFromCodeCoverage]
	public class AzureADUser : IAzureADObject, IEquatable<AzureADUser>
	{
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public Guid ObjectId { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Mail { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public virtual object Properties { get
			{
				return null;
			}
		}

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public MembershipAction? MembershipAction { get; set; }


        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public Guid SourceGroup { get; set; }


        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<Guid> SourceGroups { get; set; }

        public override bool Equals(object obj)
		{
			var castobj = obj as AzureADUser;
			if (castobj is null) return false;
			return castobj.ObjectId == ObjectId;
		}

		public bool Equals(AzureADUser other)
		{
			if (other is null) return false;
			return ObjectId == other.ObjectId;
		}

		public static bool operator ==(AzureADUser lhs, AzureADUser rhs)
		{
			if (lhs is null)
				return rhs is null;

			return lhs.Equals(rhs);
		}

		public static bool operator !=(AzureADUser lhs, AzureADUser rhs)
		{
			return !(lhs == rhs);
		}

		public override int GetHashCode() => ObjectId.GetHashCode();

		public override string ToString() => $"u: {ObjectId}";
	}
}
