// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { describe, expect, it, vi } from 'vitest';
import { analyzeUserQuery, mergeUsersById, createUsersFilterRequest } from './users';
import { UserEntity } from '../entities';

describe('analyzeUserQuery', () => {
  it('detects alias-like input with @', () => {
    const result = analyzeUserQuery('alice@contoso.com');
    expect(result.isAliasLike).toBe(true);
    expect(result.isNicknameLike).toBe(false);
  });

  it('detects nickname-like input with dots', () => {
    const result = analyzeUserQuery('alice.smith');
    expect(result.isNicknameLike).toBe(true);
    expect(result.isAliasLike).toBe(false);
    expect(result.isAlphaWord).toBe(false);
  });

  it('detects single alpha word', () => {
    const result = analyzeUserQuery('Alice');
    expect(result.isAlphaWord).toBe(true);
    expect(result.isAliasLike).toBe(false);
    expect(result.isNicknameLike).toBe(false);
  });

  it('detects multi-token input', () => {
    const result = analyzeUserQuery('Alice Smith');
    expect(result.nameTokens).toEqual(['Alice', 'Smith']);
    expect(result.isAlphaWord).toBe(false);
  });

  it('escapes single quotes for filter', () => {
    const result = analyzeUserQuery("O'Brien");
    expect(result.filterLiteral).toBe("O''Brien");
  });

  it('removes double quotes for search', () => {
    const result = analyzeUserQuery('"test"');
    expect(result.searchSafe).toBe('test');
  });

  it('trims whitespace', () => {
    const result = analyzeUserQuery('  hello  ');
    expect(result.trimmed).toBe('hello');
  });

  it('handles alphanumeric input as nickname-like', () => {
    const result = analyzeUserQuery('user123');
    expect(result.isNicknameLike).toBe(true);
  });
});

describe('createUsersFilterRequest', () => {
  const mockGet = vi.fn().mockResolvedValue({ data: { value: [] } });
  const httpClient = { get: mockGet } as any;

  it('uses mail/UPN filter for alias-like input', () => {
    createUsersFilterRequest(httpClient, { isAliasLike: true, isNicknameLike: false, isNameSingleToken: false, nameTokens: ['alice@co'], filterLiteral: 'alice@co' });
    expect(mockGet).toHaveBeenCalled();
    const params = mockGet.mock.calls[0][1].params;
    expect(params.$filter).toContain("startswith(mail,'alice@co')");
  });

  it('uses mailNickname filter for nickname-like input', () => {
    mockGet.mockClear();
    createUsersFilterRequest(httpClient, { isAliasLike: false, isNicknameLike: true, isNameSingleToken: false, nameTokens: ['alice.s'], filterLiteral: 'alice.s' });
    const params = mockGet.mock.calls[0][1].params;
    expect(params.$filter).toContain("startswith(mailNickname,'alice.s')");
  });

  it('uses displayName/givenName/surname for single name token', () => {
    mockGet.mockClear();
    createUsersFilterRequest(httpClient, { isAliasLike: false, isNicknameLike: false, isNameSingleToken: true, nameTokens: ['Alice'], filterLiteral: 'Alice' });
    const params = mockGet.mock.calls[0][1].params;
    expect(params.$filter).toContain("startswith(displayName,'Alice')");
    expect(params.$filter).toContain("startswith(givenName,'Alice')");
  });

  it('uses pairwise givenName/surname for multi-token alpha', () => {
    mockGet.mockClear();
    createUsersFilterRequest(httpClient, { isAliasLike: false, isNicknameLike: false, isNameSingleToken: false, nameTokens: ['Alice', 'Smith'], filterLiteral: 'Alice Smith' });
    const params = mockGet.mock.calls[0][1].params;
    expect(params.$filter).toContain("startswith(givenName,'Alice')");
    expect(params.$filter).toContain("startswith(surname,'Smith')");
  });

  it('returns undefined when no filter strategy matches', () => {
    mockGet.mockClear();
    const result = createUsersFilterRequest(httpClient, { isAliasLike: false, isNicknameLike: false, isNameSingleToken: false, nameTokens: ['a'], filterLiteral: 'a' });
    expect(result).toBeUndefined();
  });
});

describe('mergeUsersById', () => {
  const user = (id: string): UserEntity => ({ id, displayName: id } as any);

  it('merges arrays removing duplicates', () => {
    const result = mergeUsersById([[user('a'), user('b')], [user('b'), user('c')]]);
    expect(result).toHaveLength(3);
    expect(result.map(u => u.id)).toEqual(['a', 'b', 'c']);
  });

  it('respects limit', () => {
    const result = mergeUsersById([[user('a'), user('b'), user('c')]], 2);
    expect(result).toHaveLength(2);
  });

  it('returns empty for empty input', () => {
    expect(mergeUsersById([])).toEqual([]);
  });

  it('handles single array', () => {
    const result = mergeUsersById([[user('x')]]);
    expect(result).toHaveLength(1);
  });
});
