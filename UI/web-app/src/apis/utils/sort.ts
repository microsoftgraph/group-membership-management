// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { UserEntity } from '../entities';

// Sort users so prefix matches on displayName appear first,
// then word-boundary matches, then other fuzzy hits.
export function sortUsersByPrefix(users: UserEntity[], query: string): UserEntity[] {
  const queryLower = (query || '').toLowerCase();
  const wordBoundaryRegex = new RegExp(`\\b${queryLower}`, 'i');
  const computeRank = (u: UserEntity) => {
    const displayNameLower = (u.displayName || '').toLowerCase();
    if (displayNameLower.startsWith(queryLower)) return 0;
    if (wordBoundaryRegex.test(displayNameLower)) return 1;
    return 2;
  };
  return users.slice().sort((a, b) => computeRank(a) - computeRank(b));
}
