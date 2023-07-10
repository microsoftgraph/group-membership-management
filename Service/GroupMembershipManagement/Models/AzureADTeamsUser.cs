// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Diagnostics.CodeAnalysis;

namespace Models.Entities
{
    public class TeamsUserProperties
	{
		public string ConversationMemberId { get; set; }
	}
    [ExcludeFromCodeCoverage]
    public class AzureADTeamsUser : AzureADUser, IEquatable<AzureADTeamsUser>
    {
        public string ConversationMemberId { get; set; }

        public override object Properties
		{
            get
            {
                return new TeamsUserProperties 
				{ 
					ConversationMemberId = ConversationMemberId
				};
            }
        }


		public override bool Equals(object obj)
		{
			AzureADTeamsUser castObj = obj as AzureADTeamsUser;
			if (castObj is null) return false;
			return castObj.ObjectId == ObjectId && castObj.ConversationMemberId == ConversationMemberId;
		}

		public bool Equals(AzureADTeamsUser other)
		{
			if (other is null) return false;
			return ObjectId == other.ObjectId && ConversationMemberId == other.ConversationMemberId;
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

		public override int GetHashCode() => HashCode.Combine(ObjectId.GetHashCode(), ConversationMemberId.GetHashCode());

		public override string ToString() => $"u: {ObjectId} t: {ConversationMemberId}";
    }
}

