// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Models.Entities
{
    [ExcludeFromCodeCoverage]
    public class AzureADTeamsUser : AzureADUser, IEquatable<AzureADTeamsUser>
    {
      public string TeamsId { get; set; }

        public override bool Equals(object obj)
		{
			AzureADTeamsUser castObj = obj as AzureADTeamsUser;
			if (castObj is null) return false;
			return castObj.ObjectId == ObjectId && castObj.TeamsId == TeamsId;
		}

		public bool Equals(AzureADTeamsUser other)
		{
			if (other is null) return false;
			return ObjectId == other.ObjectId && TeamsId == other.TeamsId;
		}

		public static bool operator ==(AzureADTeamsUser lhs, AzureADTeamsUser rhs)
		{
			if (lhs is null)
				return rhs is null;

			return lhs.Equals(rhs);
		}

		public static bool operator !=(AzureADTeamsUser lhs, AzureADTeamsUser rhs)
		{
			return !(lhs == rhs);
		}

		public override int GetHashCode() => HashCode.Combine(ObjectId.GetHashCode(), TeamsId.GetHashCode());

		public override string ToString() => $"u: {ObjectId} t: {TeamsId}";
    }
}

