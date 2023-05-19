// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Models.Entities
{
    public class AzureADTeamsChannel : AzureADGroup, IEquatable<AzureADTeamsChannel>
    {
        public string ChannelId { get; init; }

		public override bool Equals(object obj)
		{
			var castObj = obj as AzureADTeamsChannel;
			if (castObj is null) return false;
			return castObj.ObjectId == ObjectId && castObj.ChannelId == ChannelId;
		}

		public bool Equals(AzureADTeamsChannel other)
		{
			if (other is null) return false;
			return ObjectId == other.ObjectId && ChannelId == other.ChannelId;
		}

		public static bool operator ==(AzureADTeamsChannel lhs, AzureADTeamsChannel rhs)
		{
			if (lhs is null)
				return rhs is null;

			return lhs.Equals(rhs);
		}

		public static bool operator !=(AzureADTeamsChannel lhs, AzureADTeamsChannel rhs)
		{
			return !(lhs == rhs);
		}

		public override int GetHashCode() => HashCode.Combine(ObjectId.GetHashCode(), ChannelId.GetHashCode());

		public override string ToString() => $"g: {ObjectId} t: {ChannelId}";
    }
}

