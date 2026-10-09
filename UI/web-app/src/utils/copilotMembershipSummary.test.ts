// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { describe, expect, it } from 'vitest';
import { formatMembershipDescription } from './copilotMembershipSummary';

describe('formatMembershipDescription', () => {
    it.each([
        ['Your current membership includes all users in **Group A**.', 'Includes all users in **Group A**.'],
        ['The current membership includes reports, excluding contractors.', 'Includes reports, excluding contractors.'],
        ['**Your current membership includes all reports.**', '**Includes all reports.**'],
        ['**Your current membership** includes all reports.', 'Includes all reports.'],
        ['Includes reports.\nExcludes interns.', 'Includes reports.\nExcludes interns.'],
        ['Excludes members of Group B.', 'Excludes members of Group B.'],
        ['Includes the group named "Your current membership includes".', 'Includes the group named "Your current membership includes".'],
        ['Introduction.\n\n**Proposed Membership:** **Your current membership includes reports.\nExcludes interns.**\n\nClick Apply & Review.',
            '**Includes reports.\nExcludes interns.**'],
    ])('formats %s without changing membership criteria', (response, expected) => {
        expect(formatMembershipDescription(response)).toBe(expected);
    });
});
