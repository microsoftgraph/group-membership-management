// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { describe, expect, it } from 'vitest';
import Ajv from 'ajv';
import Query from './Query.json';

// Guards the advanced-view query schema used by the query validation hook. A TeamsChannel source
// part must be accepted here or a valid query would be flagged invalid and could not be saved.
describe('Query.json schema', () => {
  const ajv = new Ajv();
  const validate = ajv.compile(Query);

  it('accepts a TeamsChannelMembership source combined with a group source', () => {
    const query = [
      { type: 'GroupMembership', source: '12345678-1234-1234-8234-123456789abc' },
      {
        type: 'TeamsChannelMembership',
        source: { objectId: '87654321-4321-4321-8321-cba987654321', channelId: '19:abc@thread.tacv2' },
        exclusionary: false,
      },
    ];
    expect(validate(query)).toBe(true);
  });

  it('rejects a TeamsChannelMembership source missing channelId', () => {
    const query = [
      { type: 'TeamsChannelMembership', source: { objectId: '87654321-4321-4321-8321-cba987654321' } },
    ];
    expect(validate(query)).toBe(false);
  });

  it('rejects a TeamsChannelMembership source with an unexpected property', () => {
    const query = [
      {
        type: 'TeamsChannelMembership',
        source: { objectId: 'g', channelId: 'c', extra: 'nope' },
      },
    ];
    expect(validate(query)).toBe(false);
  });
});
