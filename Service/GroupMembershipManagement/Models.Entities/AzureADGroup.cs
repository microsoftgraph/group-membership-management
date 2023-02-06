// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Diagnostics.CodeAnalysis;

namespace Models.Entities
{
	[ExcludeFromCodeCoverage]
	public class AzureADGroup : IAzureADObject, IEquatable<AzureADGroup>
	{
		public Guid ObjectId { get; set; }

		public override bool Equals(object obj)
		{
			if (obj is null) return false;
			return obj is AzureADGroup && (obj as AzureADGroup).ObjectId == ObjectId;
		}

		public bool Equals(AzureADGroup other)
		{
			if (other is null) return false;
			return ObjectId == other.ObjectId;
		}

		public static bool operator ==(AzureADGroup lhs, AzureADGroup rhs)
		{
			if (lhs is null)
				return rhs is null;

			return lhs.Equals(rhs);
		}

		public static bool operator !=(AzureADGroup lhs, AzureADGroup rhs)
		{
			return !(lhs == rhs);
		}

		public override int GetHashCode() => ObjectId.GetHashCode();

		public override string ToString() => $"g: {ObjectId}";

	}
}
