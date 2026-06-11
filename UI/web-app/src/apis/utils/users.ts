// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { Axios } from 'axios';
import { GraphResponseEntity, UserEntity } from '../entities';

export type NormalizedUsersResponse = { data: { value: UserEntity[] } };

/**
 * Analyze a freeform user-entered query to inform Graph /users request heuristics.
 *
 * Heuristics:
 * - alias-like: contains '@' → treat as mail/UPN alias input
 * - nickname-like: mixed alphanumerics or . _ - → prefer mailNickname/mail/UPN prefix filters
 * - single alpha token (len>=3): prefer name-field prefix filters (displayName, givenName, surname)
 * - multi-token alphabetic: pairwise givenName/surname prefix filters, plus displayName prefix
 *
 * Also returns sanitized variants for safe population of $filter (escaped single quotes)
 * and $search (double quotes removed to avoid query grammar conflicts).
 */
export function analyzeUserQuery(raw: string) {
  const trimmed = raw.trim();
  const filterLiteral = trimmed.replace(/'/g, "''");
  const searchSafe = trimmed.replace(/"/g, '');
  const isAliasLike = /@/.test(trimmed);
  const tokens = trimmed.split(/\s+/);
  const nameTokens = tokens.filter(t => t && t.trim().length > 0);
  const isAlphaWord = /^[A-Za-z]+$/.test(trimmed);
  const isNicknameLike = !isAliasLike && !isAlphaWord && /^[A-Za-z0-9._-]+$/.test(trimmed);
  return { trimmed, filterLiteral, searchSafe, isAliasLike, nameTokens, isAlphaWord, isNicknameLike };
}

/**
 * Create a fuzzy Graph /users $search request over mail, displayName, and userPrincipalName.
 * Uses ConsistencyLevel:eventual and $select to limit payload size.
 */
export function createUsersSearchRequest(httpClient: Axios, searchSafe: string) {
  return httpClient.get<GraphResponseEntity<UserEntity[]>>(`/users`, {
    params: {
      $select: 'displayName,mail,id,userPrincipalName',
      $search: `"mail:${searchSafe}" OR "displayName:${searchSafe}" OR "userPrincipalName:${searchSafe}"`,
      $top: 10,
    },
    headers: { 'ConsistencyLevel': 'eventual' },
  });
}

/**
 * Create a targeted Graph /users $filter request for precise prefix matching.
 * Selects filter strategy based on analyzeUserQuery() heuristics and escapes literals.
 * Returns undefined when a precise filter is not appropriate (very short or ambiguous input).
 */
export function createUsersFilterRequest(httpClient: Axios, opts: {
  isAliasLike: boolean;
  isNicknameLike: boolean;
  isNameSingleToken: boolean;
  nameTokens: string[];
  filterLiteral: string;
}) {
  const { isAliasLike, isNicknameLike, isNameSingleToken, nameTokens, filterLiteral } = opts;
  if (isAliasLike) {
    return httpClient.get<GraphResponseEntity<UserEntity[]>>(`/users`, {
      params: {
        $select: 'displayName,mail,id,userPrincipalName',
        $filter: `startswith(mail,'${filterLiteral}') or startswith(userPrincipalName,'${filterLiteral}')`,
        $top: 10,
      },
      headers: { 'ConsistencyLevel': 'eventual' },
    });
  }
  if (isNicknameLike) {
    return httpClient.get<GraphResponseEntity<UserEntity[]>>(`/users`, {
      params: {
        $select: 'displayName,mail,id,userPrincipalName,mailNickname',
        $filter:
          `startswith(mailNickname,'${filterLiteral}') or ` +
          `startswith(userPrincipalName,'${filterLiteral}') or ` +
          `startswith(mail,'${filterLiteral}')`,
        $top: 10,
      },
      headers: { 'ConsistencyLevel': 'eventual' },
    });
  }
  if (isNameSingleToken) {
    return httpClient.get<GraphResponseEntity<UserEntity[]>>(`/users`, {
      params: {
        $select: 'displayName,mail,id,userPrincipalName',
        $filter:
          `startswith(displayName,'${filterLiteral}') or ` +
          `startswith(givenName,'${filterLiteral}') or ` +
          `startswith(surname,'${filterLiteral}')`,
        $top: 10,
      },
      headers: { 'ConsistencyLevel': 'eventual' },
    });
  }
  if (nameTokens.length >= 2) {
    const firstRaw = nameTokens[0] ?? '';
    const lastRaw = nameTokens[nameTokens.length - 1] ?? '';
    const bothAlpha = /^[A-Za-z]+$/.test(firstRaw) && /^[A-Za-z]+$/.test(lastRaw);
    const firstToken = bothAlpha ? firstRaw.replace(/'/g, "''") : '';
    const lastToken = bothAlpha ? lastRaw.replace(/'/g, "''") : '';
    const pairClauses = firstToken && lastToken
      ? [
          `(startswith(givenName,'${firstToken}') and startswith(surname,'${lastToken}'))`,
          `(startswith(givenName,'${lastToken}') and startswith(surname,'${firstToken}'))`
        ]
      : [];
    const filterExpr = [`startswith(displayName,'${filterLiteral}')`, ...pairClauses].join(' or ');
    return httpClient.get<GraphResponseEntity<UserEntity[]>>(`/users`, {
      params: {
        $select: 'displayName,mail,id,userPrincipalName',
        $filter: filterExpr,
        $top: 10,
      },
      headers: { 'ConsistencyLevel': 'eventual' },
    });
  }
  return undefined;
}

export function normalizeUsersResponse(p: Promise<{ data: { value: UserEntity[] } }>): Promise<NormalizedUsersResponse> {
  return p.then(r => ({ data: r.data }));
}

export function mergeUsersById(arrays: UserEntity[][], limit = 10): UserEntity[] {
  const merged: UserEntity[] = [];
  const seenIds = new Set<string>();
  for (const arr of arrays) {
    for (const u of arr) {
      if (!seenIds.has(u.id)) {
        merged.push(u);
        seenIds.add(u.id);
        if (merged.length >= limit) return merged;
      }
    }
  }
  return merged;
}

export async function mapUsersToPersonasWithPhotos(httpClient: Axios, users: UserEntity[]) {
  const usersWithPhotos = await Promise.allSettled(users.map(async (user) => {
    try {
      const photoResponse = await httpClient.get(`/users/${user.id}/photo/$value`, { responseType: 'blob' });
      const photoUrl = URL.createObjectURL(photoResponse.data);
      return { ...user, photoUrl } as UserEntity & { photoUrl: string | null };
    } catch {
      return { ...user, photoUrl: null } as UserEntity & { photoUrl: string | null };
    }
  }));

  return usersWithPhotos.map((result, index) => {
    const user = result.status === 'fulfilled' ? result.value : { ...(users[index] as any), photoUrl: null };
    return {
      key: index,
      text: user.displayName,
      secondaryText: (user as any).mail ?? (user as any).userPrincipalName,
      id: (user as any).id,
      imageUrl: (user as any).photoUrl
    };
  });
}
