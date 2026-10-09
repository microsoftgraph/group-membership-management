// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export const extractProposedMembership = (response: string): string | undefined =>
    response.match(/(?:^|\n)\s*(?:\*\*)?Proposed Membership:(?:\*\*)?[ \t]*([\s\S]*?)(?=\r?\n[ \t]*\r?\n|$)/i)?.[1]?.trim() || undefined;

export const formatMembershipDescription = (response: string): string =>
    (extractProposedMembership(response) ?? response.trim())
        .replace(/^(\*\*)?(?:your|the) current membership includes\b/i, '$1Includes')
        .replace(/^\*\*(?:your|the) current membership\*\*\s+includes\b/i, 'Includes');
