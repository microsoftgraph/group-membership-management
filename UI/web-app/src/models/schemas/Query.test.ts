// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { describe, expect, it } from 'vitest';
import Ajv from 'ajv';
import Query from './Query.json';
import SqlMembershipSchema from './SqlMembershipSchema.json';

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

const sqlMembershipPart = {
  type: 'SqlMembership',
  source: {
    filter: "users.EmployeeType_Code = 'FTE' AND agents.BlueprintId = 'approved'",
  },
  exclusionary: false,
};

const identityTypesSchemas = [
  {
    name: 'composite query schema',
    schema: Query,
    wrap: (part: object) => [part],
  },
  {
    name: 'SQL membership schema',
    schema: SqlMembershipSchema,
    wrap: (part: object) => part,
  },
];

describe.each(identityTypesSchemas)('$name identityTypes compatibility', ({ schema, wrap }) => {
  const ajv = new Ajv();
  const validate = ajv.compile(schema);

  it.each([
    ['omitted', undefined],
    ['users only', ['users']],
    ['agents only', ['agents']],
    ['users then agents', ['users', 'agents']],
    ['agents then users', ['agents', 'users']],
  ])('accepts %s', (_description, identityTypes) => {
    const part = identityTypes === undefined
      ? { ...sqlMembershipPart }
      : { ...sqlMembershipPart, identityTypes };

    expect(validate(wrap(part))).toBe(true);
  });

  it.each([
    ['an empty array', []],
    ['duplicate values', ['users', 'users']],
    ['wrong case', ['Users']],
    ['an unsupported value', ['servicePrincipals']],
    ['a scalar', 'users'],
    ['null', null],
    ['a non-string value', [42]],
  ])('rejects %s', (_description, identityTypes) => {
    expect(validate(wrap({ ...sqlMembershipPart, identityTypes }))).toBe(false);
  });
});

describe('identityTypes scope', () => {
  const ajv = new Ajv();
  const validate = ajv.compile(Query);

  it.each([
    ['GroupMembership', '123e4567-e89b-12d3-a456-426614174000'],
    ['GroupOwnership', ['All']],
    ['PlaceMembership', 'building-1'],
    ['TeamsChannelMembership', { objectId: 'team-1', channelId: 'channel-1' }],
  ])('rejects identityTypes on %s parts', (type, source) => {
    expect(validate([{ type, source }])).toBe(true);
    expect(validate([{ type, source, identityTypes: ['users'] }])).toBe(false);
  });
});
