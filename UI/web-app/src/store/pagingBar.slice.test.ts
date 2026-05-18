// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { describe, expect, it, beforeEach, vi } from 'vitest';
import pagingBarReducer, {
  setPageSize,
  setPageNumber,
  setFilterStatus,
  setFilterDestinationName,
  setFilterDestinationOwnerPersona,
  setFilterActionRequired,
  setFilterDestinationId,
  setFilterDestinationType,
  resetFilters,
  setCustomSortBy,
  setSortKey,
  setIsSortedDescending,
  selectPagingOptions,
  PagingBarState,
} from './pagingBar.slice';
import { fetchJobs } from './jobs.api';

// Mock localStorage to prevent side effects
beforeEach(() => {
  vi.stubGlobal('localStorage', {
    getItem: vi.fn().mockReturnValue(null),
    setItem: vi.fn(),
    removeItem: vi.fn(),
    clear: vi.fn(),
    length: 0,
    key: vi.fn(),
  });
});

const initial: PagingBarState = pagingBarReducer(undefined, { type: '@@INIT' });

const buildRootState = (overrides: Partial<PagingBarState> = {}) =>
  ({ pagingBar: { ...initial, ...overrides } } as any);

describe('pagingBar.slice — reducers', () => {
  it('setPageSize updates pageSize and resets pageNumber to 1', () => {
    const seeded = { ...initial, pageNumber: 5 };
    const state = pagingBarReducer(seeded, setPageSize('25'));
    expect(state.pageSize).toBe('25');
    expect(state.pageNumber).toBe(1);
  });

  it('setPageNumber updates pageNumber', () => {
    const state = pagingBarReducer(initial, setPageNumber(3));
    expect(state.pageNumber).toBe(3);
  });

  it('setFilterStatus updates filterStatus and resets pageNumber', () => {
    const seeded = { ...initial, pageNumber: 5 };
    const state = pagingBarReducer(seeded, setFilterStatus('Enabled'));
    expect(state.filterStatus).toBe('Enabled');
    expect(state.pageNumber).toBe(1);
  });

  it('setFilterActionRequired updates filter and resets pageNumber', () => {
    const seeded = { ...initial, pageNumber: 3 };
    const state = pagingBarReducer(seeded, setFilterActionRequired('Idle'));
    expect(state.filterActionRequired).toBe('Idle');
    expect(state.pageNumber).toBe(1);
  });

  it('setFilterDestinationId resets pageNumber', () => {
    const seeded = { ...initial, pageNumber: 4 };
    const state = pagingBarReducer(seeded, setFilterDestinationId('abc-123'));
    expect(state.filterDestinationId).toBe('abc-123');
    expect(state.pageNumber).toBe(1);
  });

  it('setFilterDestinationType resets pageNumber', () => {
    const seeded = { ...initial, pageNumber: 2 };
    const state = pagingBarReducer(seeded, setFilterDestinationType('GroupMembership'));
    expect(state.filterDestinationType).toBe('GroupMembership');
    expect(state.pageNumber).toBe(1);
  });

  it('setFilterDestinationName resets pageNumber', () => {
    const seeded = { ...initial, pageNumber: 2 };
    const state = pagingBarReducer(seeded, setFilterDestinationName('My Group'));
    expect(state.filterDestinationName).toBe('My Group');
    expect(state.pageNumber).toBe(1);
  });

  describe('setFilterDestinationOwnerPersona', () => {
    it('sets owner and persona from a persona object', () => {
      const persona = { key: 1, text: 'Jane', secondaryText: 'jane@ex.com', id: 'owner-id' };
      const state = pagingBarReducer(initial, setFilterDestinationOwnerPersona(persona));
      expect(state.filterDestinationOwner).toBe('owner-id');
      expect(state.filterDestinationOwnerPersona).toEqual(persona);
      expect(state.pageNumber).toBe(1);
    });

    it('clears owner and persona when payload is null', () => {
      const seeded = {
        ...initial,
        filterDestinationOwner: 'owner-id',
        filterDestinationOwnerPersona: { key: 1, text: 'J', secondaryText: '', id: 'owner-id' },
      };
      const state = pagingBarReducer(seeded, setFilterDestinationOwnerPersona(null));
      expect(state.filterDestinationOwner).toBeUndefined();
      expect(state.filterDestinationOwnerPersona).toBeUndefined();
    });
  });

  it('resetFilters clears all filter fields and resets pageNumber', () => {
    const seeded: PagingBarState = {
      ...initial,
      pageNumber: 5,
      filterDestinationId: 'id',
      filterDestinationType: 'type',
      filterDestinationName: 'name',
      filterDestinationOwner: 'owner',
      filterDestinationOwnerPersona: { key: 1, text: 't', secondaryText: 's', id: 'o' },
      filterActionRequired: 'Idle',
      filterStatus: 'Enabled',
    };
    const state = pagingBarReducer(seeded, resetFilters());
    expect(state.filterDestinationId).toBeUndefined();
    expect(state.filterDestinationType).toBeUndefined();
    expect(state.filterDestinationName).toBeUndefined();
    expect(state.filterDestinationOwner).toBeUndefined();
    expect(state.filterDestinationOwnerPersona).toBeUndefined();
    expect(state.filterActionRequired).toBeUndefined();
    expect(state.filterStatus).toBeUndefined();
    expect(state.pageNumber).toBe(1);
  });
});

describe('pagingBar.slice — fetchJobs.fulfilled extraReducer', () => {
  it('updates totalNumberOfPages', () => {
    const state = pagingBarReducer(
      initial,
      fetchJobs.fulfilled({ totalNumberOfPages: 5, jobs: [] } as any, 'req1', undefined as never)
    );
    expect(state.totalNumberOfPages).toBe(5);
  });

  it('resets pageNumber when current page exceeds new total', () => {
    const seeded = { ...initial, pageNumber: 10 };
    const state = pagingBarReducer(
      seeded,
      fetchJobs.fulfilled({ totalNumberOfPages: 3, jobs: [] } as any, 'req1', undefined as never)
    );
    expect(state.pageNumber).toBe(1);
  });

  it('preserves pageNumber when within range', () => {
    const seeded = { ...initial, pageNumber: 2 };
    const state = pagingBarReducer(
      seeded,
      fetchJobs.fulfilled({ totalNumberOfPages: 5, jobs: [] } as any, 'req1', undefined as never)
    );
    expect(state.pageNumber).toBe(2);
  });
});

describe('selectPagingOptions — OData filter building', () => {
  it('returns no filter or orderBy with defaults', () => {
    const result = selectPagingOptions(buildRootState());
    expect(result.filter).toBeUndefined();
    expect(result.orderBy).toBeUndefined();
    expect(result.itemsToSkip).toBe(0);
    expect(result.pageSize).toBe(10);
  });

  it('builds orderBy from sortKey', () => {
    const result = selectPagingOptions(buildRootState({ sortKey: 'status' }));
    expect(result.orderBy).toBe('status');
  });

  it('adds desc to orderBy when isSortedDescending is true', () => {
    const result = selectPagingOptions(
      buildRootState({ sortKey: 'status', isSortedDescending: true })
    );
    expect(result.orderBy).toBe('status desc');
  });

  it('excludes targetGroupName from orderBy (handled as customSortBy)', () => {
    const result = selectPagingOptions(buildRootState({ sortKey: 'targetGroupName' }));
    expect(result.orderBy).toBeUndefined();
  });

  it('excludes lastModifiedTime from orderBy (handled as customSortBy)', () => {
    const result = selectPagingOptions(buildRootState({ sortKey: 'lastModifiedTime' }));
    expect(result.orderBy).toBeUndefined();
  });

  it('builds filter for filterDestinationId', () => {
    const result = selectPagingOptions(buildRootState({ filterDestinationId: 'abc-123' }));
    expect(result.filter).toContain('Group/GroupId eq abc-123');
  });

  it('builds filter for filterActionRequired (excludes All)', () => {
    const result = selectPagingOptions(buildRootState({ filterActionRequired: 'Idle' }));
    expect(result.filter).toContain("status eq 'Idle'");
  });

  it('does not add filter for filterActionRequired = All', () => {
    const result = selectPagingOptions(buildRootState({ filterActionRequired: 'All' }));
    expect(result.filter).toBeUndefined();
  });

  it('builds filter for filterDestinationType (excludes All)', () => {
    const result = selectPagingOptions(buildRootState({ filterDestinationType: 'GroupMembership' }));
    expect(result.filter).toContain("contains(Destination, 'GroupMembership')");
  });

  it('does not add filter for filterDestinationType = All', () => {
    const result = selectPagingOptions(buildRootState({ filterDestinationType: 'All' }));
    expect(result.filter).toBeUndefined();
  });

  it('escapes single quotes in filterDestinationName', () => {
    const result = selectPagingOptions(buildRootState({ filterDestinationName: "O'Brien" }));
    expect(result.filter).toContain("O''Brien");
  });

  it('builds sub-conditions for filterDestinationName with name and email', () => {
    const result = selectPagingOptions(buildRootState({ filterDestinationName: 'test' }));
    expect(result.filter).toContain('DestinationName/Name');
    expect(result.filter).toContain('DestinationEmail/Email');
  });

  it('includes GUID condition when filterDestinationName is a valid GUID', () => {
    const guid = '12345678-1234-1234-1234-123456789abc';
    const result = selectPagingOptions(buildRootState({ filterDestinationName: guid }));
    expect(result.filter).toContain('targetOfficeGroupId eq ' + guid);
  });

  it('excludes GUID condition when filterDestinationName is not a valid GUID', () => {
    const result = selectPagingOptions(buildRootState({ filterDestinationName: 'not-a-guid' }));
    expect(result.filter).not.toContain('targetOfficeGroupId');
  });

  it('builds Enabled filter with Idle or InProgress statuses', () => {
    const result = selectPagingOptions(buildRootState({ filterStatus: 'Enabled' }));
    expect(result.filter).toContain("status eq 'Idle'");
    expect(result.filter).toContain("status eq 'InProgress'");
  });

  it('builds Disabled filter negating Idle and InProgress', () => {
    const result = selectPagingOptions(buildRootState({ filterStatus: 'Disabled' }));
    expect(result.filter).toContain('not (');
  });

  it('builds owner filter', () => {
    const result = selectPagingOptions(buildRootState({ filterDestinationOwner: 'owner-id' }));
    expect(result.filter).toContain('DestinationOwners/any(o: o/ObjectId eq owner-id)');
  });

  it('combines multiple filters with "and"', () => {
    const result = selectPagingOptions(
      buildRootState({
        filterDestinationId: 'id1',
        filterDestinationOwner: 'owner1',
      })
    );
    expect(result.filter).toContain(' and ');
  });

  it('calculates itemsToSkip from pageNumber and pageSize', () => {
    const result = selectPagingOptions(
      buildRootState({ pageNumber: 3, pageSize: '25' })
    );
    expect(result.itemsToSkip).toBe(50);
    expect(result.pageSize).toBe(25);
  });

  it('returns customSortBy for targetGroupName', () => {
    const result = selectPagingOptions(buildRootState({ customSortBy: 'targetGroupName' }));
    expect(result.customSortBy).toBe('targetGroupName');
  });

  it('returns customSortBy for lastModifiedTime', () => {
    const result = selectPagingOptions(buildRootState({ customSortBy: 'lastModifiedTime' }));
    expect(result.customSortBy).toBe('lastModifiedTime');
  });

  it('returns undefined customSortBy for other values', () => {
    const result = selectPagingOptions(buildRootState({ customSortBy: 'status' }));
    expect(result.customSortBy).toBeUndefined();
  });
});
