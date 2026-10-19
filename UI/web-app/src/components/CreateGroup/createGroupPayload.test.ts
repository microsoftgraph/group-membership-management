// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { describe, expect, it } from 'vitest';
import { NewGroupType } from '../../models/NewGroupType';
import { buildCreateGroupRequest, deriveGroupAlias } from './createGroupPayload';

describe('create group functional payload', () => {
  it.each([NewGroupType.Microsoft365, NewGroupType.Security])(
    'allowlists functional fields for group type %s',
    groupType => {
      const draft = {
        groupName: 'Finance Team',
        groupAlias: 'custom+alias',
        groupType,
        description: '',
        sensitivityLabel: 'example',
        visibility: 'Public',
        policy: 'owner',
        welcomeEmails: true,
        autoFollow: false,
        showInOutlook: false,
        useNameAsAlias: false,
      };

      expect(buildCreateGroupRequest(draft)).toEqual({
        groupName: 'Finance Team',
        groupAlias: groupType === NewGroupType.Security ? 'FinanceTeam' : 'custom+alias',
        groupType,
        description: '',
      });
    }
  );

  it.each([undefined, null, '', ' ', 'A team description'])(
    'preserves description compatibility for %s',
    description => {
      const payload = buildCreateGroupRequest({
        groupName: 'Finance',
        groupAlias: 'finance',
        groupType: NewGroupType.Microsoft365,
        description,
      });
      expect(payload.description).toBe(description);
      expect(Object.prototype.hasOwnProperty.call(payload, 'description')).toBe(description !== undefined);
    }
  );

  it('retains the existing alias derivation without new normalization or fallback', () => {
    expect(deriveGroupAlias('Finance+Ops!#$%&\'*-/=?^_`{|}~@()\\[]";:<>, \u00e9\u007f'))
      .toBe('Finance+Ops!#$%&\'*-/=?^_`{|}~');
    expect(deriveGroupAlias('@ ')).toBe('');
    expect(deriveGroupAlias('a'.repeat(65))).toHaveLength(65);
  });
});
