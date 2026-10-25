// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Caching.Memory;
using Services.WebApi.Contracts;
using System.Text.RegularExpressions;

namespace Services.WebApi;

internal sealed class OrgLeaderConfirmationGate
{
    private const int MaxCandidatesPerConversation = 100;
    private const int MaxPendingGroups = 20;
    private const int MaxCandidatesPerGroup = 10;

    internal sealed record Candidate(string Name, string Email, string ObjectId);
    private sealed record State(Candidate[][] Pending, Candidate[] Confirmed, Candidate[] Validated, string[] Rejected,
        string[] AuthorizedLookups);

    private static readonly MemoryCache Conversations = new(new MemoryCacheOptions { SizeLimit = 2000 });
    private static readonly Regex EmailPattern = new(
        @"[A-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Z0-9](?:[A-Z0-9-]{0,61}[A-Z0-9])?(?:\.[A-Z0-9](?:[A-Z0-9-]{0,61}[A-Z0-9])?)+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex AffirmativePattern = new(
        @"^(?:yes|yep|yeah|correct|confirmed|confirm|looks good|that's right|that is right)(?:[\s,]+(?:please|use me(?: as (?:the )?org(?:anization)? leader)?|use them|use both|that's right|that is right))*[.! ]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex RejectionPattern = new(
        @"^(?:no|nope|wrong|incorrect|that's wrong|that is wrong|not the right person|don't use|do not use)(?:[\s,!.].*)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex EmailSelectionRemainderPattern = new(
        @"^(?:(?:please|use|confirm|select|choose|i|want|to|the|org|organization|leader|as|my|me|yes|yep|yeah|correct|that's|right|that|is|it|both|and)\s*)*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly object _sync = new();
    private readonly string? _key;
    private readonly string _userRequest;
    private readonly Dictionary<string, Candidate> _confirmed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Candidate> _validated = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Candidate> _known = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Candidate> _existingRoots = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _rejected = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _authorizedLookups = new(StringComparer.Ordinal);
    private readonly List<Candidate[]> _pending = new();
    private bool _blocked;
    private Candidate? _validationRequired;

    internal OrgLeaderConfirmationGate(string? conversationId, string? callerObjectId, string userMessage)
    {
        _userRequest = ExtractUserRequest(userMessage);
        if (!Guid.TryParse(conversationId, out var id))
            return;

        var callerKey = string.IsNullOrWhiteSpace(callerObjectId)
            ? "anonymous"
            : callerObjectId.ToLowerInvariant();
        _key = $"{callerKey}:{id:D}";
        if (Conversations.TryGetValue<State>(_key, out var state) && state != null)
        {
            foreach (var candidate in state.Confirmed)
            {
                _confirmed[candidate.Email] = candidate;
                _known[candidate.Email] = candidate;
            }
            foreach (var candidate in state.Validated)
            {
                _validated[candidate.Email] = candidate;
                _known[candidate.Email] = candidate;
            }

            foreach (var group in state.Pending)
            {
                var copy = group.ToArray();
                _pending.Add(copy);
                foreach (var candidate in copy)
                    _known[candidate.Email] = candidate;
            }
            foreach (var email in state.Rejected)
                _rejected.Add(email);
            foreach (var query in state.AuthorizedLookups)
                _authorizedLookups.Add(query);
        }

        ApplyUserConfirmation(_userRequest);
    }

    internal bool AllowLookup(string? searchQuery)
    {
        if (string.IsNullOrWhiteSpace(searchQuery))
            return true;

        var query = NormalizeLookupText(searchQuery);
        if (query.Length < 3 || IsGenericOrgScope(query))
            return false;

        lock (_sync)
        {
            if (_authorizedLookups.Contains(query))
                return true;
            if (!NormalizeLookupText(_userRequest).Contains(query, StringComparison.Ordinal))
                return false;
            if (_authorizedLookups.Count < MaxCandidatesPerConversation)
                _authorizedLookups.Add(query);
            return true;
        }
    }

    internal bool IsConfirmed(string? email, string? objectId = null)
    {
        lock (_sync)
        {
            return email != null && _confirmed.TryGetValue(email, out var candidate) &&
                (objectId == null || candidate.ObjectId.Equals(objectId, StringComparison.OrdinalIgnoreCase));
        }
    }

    internal bool TryGetConfirmed(string email, out Candidate? candidate)
    {
        lock (_sync)
        {
            var found = _confirmed.TryGetValue(email, out var value) ||
                        _existingRoots.TryGetValue(email, out value);
            candidate = value;
            return found;
        }
    }

    internal bool TryGetValidated(string email, out Candidate? candidate)
    {
        lock (_sync)
        {
            var found = _validated.TryGetValue(email, out var value);
            candidate = value;
            return found;
        }
    }

    internal bool IsValidated(string? email, string? objectId = null)
    {
        lock (_sync)
        {
            return email != null && _validated.TryGetValue(email, out var candidate) &&
                (objectId == null || candidate.ObjectId.Equals(objectId, StringComparison.OrdinalIgnoreCase));
        }
    }

    internal void MarkValidated(Candidate candidate)
    {
        lock (_sync)
        {
            if (_confirmed.TryGetValue(candidate.Email, out var confirmed) &&
                confirmed.ObjectId.Equals(candidate.ObjectId, StringComparison.OrdinalIgnoreCase))
            {
                _validated[candidate.Email] = candidate;
            }
        }
    }

    internal void Know(Candidate candidate)
    {
        lock (_sync)
            Remember(candidate);
    }

    internal void TrustExistingRoot(Candidate candidate)
    {
        lock (_sync)
        {
            if (!_existingRoots.ContainsKey(candidate.Email) &&
                _existingRoots.Count >= MaxCandidatesPerConversation)
                return;
            _existingRoots[candidate.Email] = candidate;
            Remember(candidate);
        }
    }

    internal void Found(IEnumerable<Candidate> candidates, bool isOrgLeader = true)
    {
        if (!isOrgLeader)
            return;

        lock (_sync)
        {
            var found = candidates.Take(MaxCandidatesPerGroup).ToArray();
            foreach (var candidate in found)
            {
                _rejected.Remove(candidate.Email);
            }

            var group = found
                .Where(Remember)
                .DistinctBy(candidate => candidate.Email, StringComparer.OrdinalIgnoreCase)
                .Where(candidate => !_confirmed.ContainsKey(candidate.Email))
                .ToArray();
            if (group.Length > 0 && _pending.Count < MaxPendingGroups &&
                !_pending.Any(existing => SameGroup(existing, group)))
                _pending.Add(group);
        }
    }

    internal bool AllowValidation(string? email)
    {
        lock (_sync)
        {
            if (email != null && (_existingRoots.ContainsKey(email) ||
                                  (_key != null && _confirmed.ContainsKey(email))))
                return true;

            _blocked = true;
            AddKnownCandidate(email);
            return false;
        }
    }

    internal void RequireConfirmation(string? email)
    {
        lock (_sync)
        {
            _blocked = true;
            AddKnownCandidate(email);
        }
    }

    internal void RequireValidation(string email)
    {
        lock (_sync)
        {
            _blocked = true;
            if (_known.TryGetValue(email, out var candidate))
                _validationRequired = candidate;
        }
    }

    internal string? Finish(string modelMessage)
    {
        lock (_sync)
        {
            if (ContainsLeaderConfirmationRequest(modelMessage))
            {
                foreach (var candidate in _known.Values.Where(candidate =>
                             modelMessage.Contains(candidate.Email, StringComparison.OrdinalIgnoreCase)))
                {
                    AddPendingGroup(new[] { candidate });
                }
            }

            var pending = _pending.Select(group => group.ToArray()).ToArray();
            if (_key != null)
            {
                Conversations.Set(_key, new State(
                        pending,
                        _confirmed.Values.ToArray(),
                        _validated.Values.ToArray(),
                        _rejected.ToArray(),
                        _authorizedLookups.ToArray()),
                    new MemoryCacheEntryOptions
                    {
                        Size = 1,
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
                    });
            }

            if (!_blocked && pending.Length == 0)
                return null;
            if (_key == null)
                return "Please start a new Copilot conversation so I can confirm the org leader's email before making changes.";
            if (_validationRequired != null)
            {
                return $"I have your confirmation for **{_validationRequired.Name}** ({_validationRequired.Email}), but I could not verify their org-leader eligibility. Please ask me to validate this person before making changes.";
            }
            if (pending.Length == 0)
                return "Please provide the org leader's name or email so I can look them up and ask you to confirm their email before making changes.";

            var people = string.Join("\n", pending.SelectMany(group => group)
                .DistinctBy(candidate => candidate.Email, StringComparer.OrdinalIgnoreCase)
                .Select(candidate => $"- **{candidate.Name}** ({candidate.Email})"));
            return pending.All(group => group.Length == 1)
                ? $"Please confirm these org leader emails before I make any changes:\n{people}\nReply \"yes\" to confirm, or provide the correct leader's email."
                : $"I found these possible org leaders:\n{people}\nPlease reply with the email address of each person you want to use before I make any changes.";
        }
    }

    private void ApplyUserConfirmation(string reply)
    {
        lock (_sync)
        {
            if (RejectionPattern.IsMatch(reply))
            {
                foreach (var email in _pending.SelectMany(group => group).Select(candidate => candidate.Email)
                             .Concat(_confirmed.Keys).Concat(_validated.Keys))
                {
                    if (_rejected.Count < MaxCandidatesPerConversation * 3)
                        _rejected.Add(email);
                }
                _confirmed.Clear();
                _validated.Clear();
                _pending.Clear();
                return;
            }

            if (_pending.Count == 0 || string.IsNullOrWhiteSpace(reply))
                return;

            if (AffirmativePattern.IsMatch(reply) && _pending.All(group => group.Length == 1))
            {
                foreach (var candidate in _pending.SelectMany(group => group))
                    _confirmed[candidate.Email] = candidate;
                _pending.Clear();
                return;
            }

            var matches = EmailPattern.Matches(reply);
            if (matches.Count == 0)
                return;

            var remainder = EmailPattern.Replace(reply, " ").Trim(' ', ',', ';', '.', '!', '?');
            if (!EmailSelectionRemainderPattern.IsMatch(remainder))
                return;

            var selectedEmails = matches.Select(match => match.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var pendingCandidates = _pending.SelectMany(group => group).ToArray();
            if (selectedEmails.Any(email => pendingCandidates.All(candidate =>
                    !candidate.Email.Equals(email, StringComparison.OrdinalIgnoreCase))))
                return;

            foreach (var candidate in pendingCandidates.Where(candidate => selectedEmails.Contains(candidate.Email)))
                _confirmed[candidate.Email] = candidate;

            _pending.RemoveAll(group => group.Any(candidate => selectedEmails.Contains(candidate.Email)));
        }
    }

    private void AddKnownCandidate(string? email)
    {
        if (email != null && _known.TryGetValue(email, out var candidate))
            AddPendingGroup(new[] { candidate });
    }

    private bool Remember(Candidate candidate)
    {
        if (!_known.ContainsKey(candidate.Email) && _known.Count >= MaxCandidatesPerConversation)
            return false;
        _known[candidate.Email] = candidate;
        return true;
    }

    private void AddPendingGroup(Candidate[] group)
    {
        var unconfirmed = group.Where(candidate => !_confirmed.ContainsKey(candidate.Email) &&
                                                   !_existingRoots.ContainsKey(candidate.Email) &&
                                                   !_rejected.Contains(candidate.Email)).ToArray();
        if (unconfirmed.Length > 0 && _pending.Count < MaxPendingGroups &&
            !_pending.Any(existing => SameGroup(existing, unconfirmed)))
            _pending.Add(unconfirmed);
    }

    private static bool SameGroup(Candidate[] left, Candidate[] right) =>
        left.Select(candidate => candidate.Email).ToHashSet(StringComparer.OrdinalIgnoreCase)
            .SetEquals(right.Select(candidate => candidate.Email));

    private static bool ContainsLeaderConfirmationRequest(string message) =>
        message.Contains("confirm", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("org leader", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("organization leader", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeLookupText(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static bool IsGenericOrgScope(string query) =>
        query is "myorg" or "myteam" or "myorganization" or "mymanager" or
            "mydirectreports" or "everyoneunderm" or "everyoneinmyorg" or "everyoneinmyteam";

    private static string ExtractUserRequest(string message)
    {
        const string marker = "\n\nUser request:\n";
        var markerIndex = message.LastIndexOf(marker, StringComparison.Ordinal);
        return (markerIndex >= 0 ? message[(markerIndex + marker.Length)..] : message).Trim();
    }
}
