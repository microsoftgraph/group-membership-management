// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Models
{
    /// <summary>
    /// The set of identity types a membership rule selects, converted to and from the
    /// lower-case JSON values "users" and "agents".
    /// </summary>
    public readonly struct IdentityTypeSet : IEquatable<IdentityTypeSet>
    {
        // The order here is the order ToTypeNames returns, and sets each type's bit position.
        private static readonly (IdentityType Type, string Name)[] _typeNames =
        {
            (IdentityType.Users, "users"),
            (IdentityType.Agents, "agents"),
        };

        // Bit N is set when the type at position N of _typeNames is selected.
        private readonly int _selectedTypeBits;

        private IdentityTypeSet(int selectedTypeBits)
        {
            _selectedTypeBits = selectedTypeBits;
        }

        public static IdentityTypeSet UsersOnly => Of(IdentityType.Users);

        public static IdentityTypeSet All =>
            new IdentityTypeSet(_typeNames.Aggregate(0, (bits, entry) => bits | BitOf(entry.Type)));

        public static IReadOnlyList<string> SupportedTypeNames =>
            _typeNames.Select(entry => entry.Name).ToList();

        public static IReadOnlyList<IdentityType> SupportedTypes =>
            _typeNames.Select(entry => entry.Type).ToList();

        public bool IsEmpty => _selectedTypeBits == 0;

        /// <summary>
        /// Creates a set from identity types chosen in code, for example
        /// Of(IdentityType.Users, IdentityType.Agents) for users and agents.
        /// Passing a type twice has the same effect as passing it once. Passing no types gives an empty set.
        /// </summary>
        /// <param name="identityTypes">
        /// The types to include. Null throws <see cref="ArgumentNullException"/>. A value that is not one of the
        /// supported <see cref="IdentityType"/> values, such as (IdentityType)5, throws
        /// <see cref="ArgumentOutOfRangeException"/>.
        /// </param>
        /// <returns>A set that contains exactly the given types.</returns>
        public static IdentityTypeSet Of(params IdentityType[] identityTypes)
        {
            if (identityTypes == null)
            {
                throw new ArgumentNullException(nameof(identityTypes));
            }

            var selectedTypeBits = 0;

            foreach (var identityType in identityTypes)
            {
                selectedTypeBits |= BitOf(identityType);
            }

            return new IdentityTypeSet(selectedTypeBits);
        }

        public bool Includes(IdentityType identityType) =>
            (_selectedTypeBits & BitOf(identityType)) != 0;

        /// <summary>
        /// Returns the lower-case names of the selected types, as they are written in a query part's identityTypes
        /// list. A set of users and agents gives ["users","agents"]; users always comes before agents, whatever
        /// order the set was built in. An empty set gives an empty list.
        /// </summary>
        /// <returns>The names of the selected types, each once.</returns>
        public IReadOnlyList<string> ToTypeNames()
        {
            var selectedTypeBits = _selectedTypeBits;

            return _typeNames
                .Where(entry => (selectedTypeBits & BitOf(entry.Type)) != 0)
                .Select(entry => entry.Name)
                .ToList();
        }

        /// <summary>
        /// Converts a list of identity type names into a set, when the names are already plain strings rather than
        /// JSON; for example, names carried in a request from one function to another. ["agents","users"] gives
        /// users and agents; the order does not matter.
        /// </summary>
        /// <param name="typeNames">
        /// The names, or null when no list was given. Each must be a supported name in lower case, such as "users"
        /// or "agents", and appear only once. Each of these fails: an empty list, a null or empty entry, an unknown
        /// name, a name in the wrong case such as "Users", and a repeated name.
        /// </param>
        /// <param name="whenOmitted">
        /// The set to return when <paramref name="typeNames"/> is null. For example, pass
        /// <see cref="UsersOnly"/> so that requests created before identityTypes existed still return only users.
        /// </param>
        /// <param name="selection">The resulting set. Only meaningful when this returns true.</param>
        /// <param name="error">When this returns false, a message that says what is wrong; otherwise null.</param>
        /// <returns>True when the list is valid or null; false when it must be rejected.</returns>
        public static bool TryResolve(
            IReadOnlyList<string> typeNames,
            IdentityTypeSet whenOmitted,
            out IdentityTypeSet selection,
            out string error)
        {
            selection = default;
            error = null;

            if (typeNames == null)
            {
                selection = whenOmitted;
                return true;
            }

            if (typeNames.Count == 0)
            {
                error = $"identityTypes must not be empty; supported values are {DescribeSupportedValues()}.";
                return false;
            }

            var selectedTypeBits = 0;

            for (var index = 0; index < typeNames.Count; index++)
            {
                var typeName = typeNames[index];

                if (string.IsNullOrEmpty(typeName))
                {
                    error = $"identityTypes entry at index {index} must not be null or empty.";
                    return false;
                }

                var matchIndex = Array.FindIndex(
                    _typeNames,
                    entry => string.Equals(entry.Name, typeName, StringComparison.Ordinal));

                if (matchIndex < 0)
                {
                    error = $"identityTypes entry at index {index} is not supported; supported values are {DescribeSupportedValues()}, lower case.";
                    return false;
                }

                var typeBit = BitOf(_typeNames[matchIndex].Type);

                if ((selectedTypeBits & typeBit) != 0)
                {
                    error = $"identityTypes entry at index {index} repeats a value; each supported value may appear once.";
                    return false;
                }

                selectedTypeBits |= typeBit;
            }

            selection = new IdentityTypeSet(selectedTypeBits);
            return true;
        }

        public bool Equals(IdentityTypeSet other) =>
            _selectedTypeBits == other._selectedTypeBits;

        public override bool Equals(object obj)
        {
            if (!(obj is IdentityTypeSet))
            {
                return false;
            }

            return Equals((IdentityTypeSet)obj);
        }

        public override int GetHashCode() => _selectedTypeBits;

        public static bool operator ==(IdentityTypeSet left, IdentityTypeSet right) =>
            left.Equals(right);

        public static bool operator !=(IdentityTypeSet left, IdentityTypeSet right) =>
            !left.Equals(right);

        public override string ToString() =>
            IsEmpty ? "none" : string.Join(",", ToTypeNames());

        private static int BitOf(IdentityType identityType)
        {
            var matchIndex = Array.FindIndex(_typeNames, entry => entry.Type == identityType);

            if (matchIndex < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(identityType),
                    identityType,
                    "Unsupported identity type.");
            }

            return 1 << matchIndex;
        }

        private static string DescribeSupportedValues() =>
            string.Join(", ", _typeNames.Select(entry => $"'{entry.Name}'"));
    }
}
