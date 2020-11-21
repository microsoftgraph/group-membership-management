using System;
using System.Diagnostics.CodeAnalysis;

namespace Entities
{
	[ExcludeFromCodeCoverage]
	public class AzureADUser : IAzureADObject, IEquatable<AzureADUser>
	{
		public Guid ObjectId { get; set; }

		public override bool Equals(object obj)
		{
			if (obj is null) return false;
			return obj is AzureADUser && (obj as AzureADUser).ObjectId == ObjectId;
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
