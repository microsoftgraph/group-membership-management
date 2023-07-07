// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;



namespace Models
{
    [ExcludeFromCodeCoverage]
	public class AzureADUser : IAzureADObject, IEquatable<AzureADUser>
	{
        public Guid ObjectId { get; set; }

        public string Mail { get; set; }

        public virtual object Properties { get
			{
				return null;
			}
		}

        public MembershipAction? MembershipAction { get; set; }


        public Guid SourceGroup { get; set; }


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
