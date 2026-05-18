// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { describe, expect, it } from 'vitest';
import { sortUsersByPrefix } from './sort';
import { UserEntity } from '../entities';

const user = (displayName: string): UserEntity => ({ displayName, id: displayName, mail: '', userPrincipalName: '' } as any);

describe('sortUsersByPrefix', () => {
  it('ranks prefix matches first', () => {
    const users = [user('Bob Smith'), user('Alice Johnson'), user('Alice Adams')];
    const sorted = sortUsersByPrefix(users, 'ali');
    expect(sorted[0].displayName).toBe('Alice Johnson');
    expect(sorted[1].displayName).toBe('Alice Adams');
  });

  it('ranks word-boundary matches second', () => {
    const users = [user('XYZ Alice'), user('alice Smith'), user('Bob alice')];
    const sorted = sortUsersByPrefix(users, 'alice');
    // 'alice Smith' starts with alice → rank 0
    // 'XYZ Alice' → word boundary → rank 1
    // 'Bob alice' → word boundary → rank 1
    expect(sorted[0].displayName).toBe('alice Smith');
  });

  it('handles empty query', () => {
    const users = [user('A'), user('B')];
    const sorted = sortUsersByPrefix(users, '');
    expect(sorted).toHaveLength(2);
  });

  it('handles null displayName gracefully', () => {
    const users = [{ displayName: null, id: '1' } as any, user('Alice')];
    const sorted = sortUsersByPrefix(users, 'ali');
    expect(sorted[0].displayName).toBe('Alice');
  });
});
