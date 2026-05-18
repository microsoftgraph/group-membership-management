// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { describe, expect, it } from 'vitest';
import titleReducer, {
  clearTitles,
  upsertGeneratedTitle,
  clearGeneratedHRParts,
  clearGeneratedGroupParts,
  TitleState,
} from './title.slice';
import {
  getTitle,
  generateTitles,
  fetchOrgLeaderDetailsAndGenerateHRTitle,
  fetchGroupDetailsAndGenerateTitle,
} from './title.api';
import type { HRPart } from '../models/HRPart';
import type { ISourcePart } from '../models';

const initial: TitleState = titleReducer(undefined, { type: '@@INIT' });

const makeHRPart = (partId: string, filter = 'f', title = 't'): HRPart => ({
  partId,
  filter,
  title,
});

const makeSourcePart = (id: string): ISourcePart =>
  ({ id, title: 'T', isNew: false, isExpanded: false, query: {} } as unknown as ISourcePart);

describe('title.slice — reducers', () => {
  it('clearTitles empties the titles array', () => {
    const seeded: TitleState = { ...initial, titles: [makeHRPart('p1')] };
    const state = titleReducer(seeded, clearTitles());
    expect(state.titles).toEqual([]);
  });

  describe('upsertGeneratedTitle', () => {
    it('inserts a new title when partId is not present', () => {
      const state = titleReducer(initial, upsertGeneratedTitle(makeHRPart('p1', 'f1', 'Title 1')));
      expect(state.titles).toHaveLength(1);
      expect(state.titles[0].partId).toBe('p1');
    });

    it('updates an existing title when partId already exists', () => {
      const seeded: TitleState = { ...initial, titles: [makeHRPart('p1', 'old', 'Old')] };
      const updated = makeHRPart('p1', 'new', 'New');
      const state = titleReducer(seeded, upsertGeneratedTitle(updated));
      expect(state.titles).toHaveLength(1);
      expect(state.titles[0].title).toBe('New');
      expect(state.titles[0].filter).toBe('new');
    });

    it('does not affect other titles when upserting', () => {
      const seeded: TitleState = { ...initial, titles: [makeHRPart('p1'), makeHRPart('p2')] };
      const state = titleReducer(seeded, upsertGeneratedTitle(makeHRPart('p1', 'x', 'X')));
      expect(state.titles).toHaveLength(2);
      expect(state.titles[1].partId).toBe('p2');
    });
  });

  it('clearGeneratedHRParts empties the generatedHRParts array', () => {
    const seeded: TitleState = { ...initial, generatedHRParts: [makeSourcePart('s1')] };
    const state = titleReducer(seeded, clearGeneratedHRParts());
    expect(state.generatedHRParts).toEqual([]);
  });

  it('clearGeneratedGroupParts empties the generatedGroupParts array', () => {
    const seeded: TitleState = { ...initial, generatedGroupParts: [makeSourcePart('s1')] };
    const state = titleReducer(seeded, clearGeneratedGroupParts());
    expect(state.generatedGroupParts).toEqual([]);
  });
});

describe('title.slice — getTitle extraReducers', () => {
  it('sets isGeneratingTitle on pending', () => {
    const state = titleReducer(initial, getTitle.pending('req1', ''));
    expect(state.isGeneratingTitle).toBe(true);
  });

  it('sets title and clears loading on fulfilled', () => {
    const pending: TitleState = { ...initial, isGeneratingTitle: true };
    const state = titleReducer(pending, getTitle.fulfilled('Generated Title', 'req1', ''));
    expect(state.isGeneratingTitle).toBe(false);
    expect(state.title).toBe('Generated Title');
  });

  it('sets error and clears loading on rejected', () => {
    const pending: TitleState = { ...initial, isGeneratingTitle: true };
    const state = titleReducer(pending, getTitle.rejected(new Error('fail'), 'req1', ''));
    expect(state.isGeneratingTitle).toBe(false);
    expect(state.error).toBe('fail');
  });
});

describe('title.slice — generateTitles extraReducers', () => {
  it('sets isGeneratingTitles on pending', () => {
    const state = titleReducer(initial, generateTitles.pending('req1', []));
    expect(state.isGeneratingTitles).toBe(true);
  });

  it('sets titles on fulfilled', () => {
    const titles = [makeHRPart('p1'), makeHRPart('p2')];
    const state = titleReducer(initial, generateTitles.fulfilled(titles, 'req1', []));
    expect(state.isGeneratingTitles).toBe(false);
    expect(state.titles).toEqual(titles);
  });

  it('falls back to meta.arg on rejected', () => {
    const fallback = [makeHRPart('p1')];
    const state = titleReducer(
      initial,
      generateTitles.rejected(new Error('err'), 'req1', fallback)
    );
    expect(state.isGeneratingTitles).toBe(false);
    expect(state.titles).toEqual(fallback);
  });
});

describe('title.slice — fetchOrgLeaderDetailsAndGenerateHRTitle extraReducers', () => {
  it('sets isGeneratingHRTitle on pending', () => {
    const state = titleReducer(
      initial,
      fetchOrgLeaderDetailsAndGenerateHRTitle.pending('req1', undefined as any)
    );
    expect(state.isGeneratingHRTitle).toBe(true);
  });

  it('inserts a new generatedHRPart on fulfilled', () => {
    const part = makeSourcePart('s1');
    const state = titleReducer(
      initial,
      fetchOrgLeaderDetailsAndGenerateHRTitle.fulfilled(part, 'req1', undefined as any)
    );
    expect(state.isGeneratingHRTitle).toBe(false);
    expect(state.generatedHRParts).toHaveLength(1);
    expect(state.generatedHRParts[0].id).toBe('s1');
  });

  it('updates existing generatedHRPart on fulfilled with same id', () => {
    const existing = makeSourcePart('s1');
    const seeded: TitleState = { ...initial, generatedHRParts: [existing] };
    const updated = { ...makeSourcePart('s1'), title: 'Updated' };
    const state = titleReducer(
      seeded,
      fetchOrgLeaderDetailsAndGenerateHRTitle.fulfilled(updated, 'req1', undefined as any)
    );
    expect(state.generatedHRParts).toHaveLength(1);
    expect(state.generatedHRParts[0].title).toBe('Updated');
  });

  it('sets error on rejected', () => {
    const state = titleReducer(
      initial,
      fetchOrgLeaderDetailsAndGenerateHRTitle.rejected(new Error('oops'), 'req1', undefined as any)
    );
    expect(state.isGeneratingHRTitle).toBe(false);
    expect(state.error).toBe('oops');
  });
});

describe('title.slice — fetchGroupDetailsAndGenerateTitle extraReducers', () => {
  it('sets isGeneratingGroupTitle on pending', () => {
    const state = titleReducer(
      initial,
      fetchGroupDetailsAndGenerateTitle.pending('req1', undefined as any)
    );
    expect(state.isGeneratingGroupTitle).toBe(true);
  });

  it('inserts a new generatedGroupPart on fulfilled', () => {
    const part = makeSourcePart('g1');
    const state = titleReducer(
      initial,
      fetchGroupDetailsAndGenerateTitle.fulfilled(part, 'req1', undefined as any)
    );
    expect(state.isGeneratingGroupTitle).toBe(false);
    expect(state.generatedGroupParts).toHaveLength(1);
  });

  it('updates existing generatedGroupPart on fulfilled', () => {
    const seeded: TitleState = { ...initial, generatedGroupParts: [makeSourcePart('g1')] };
    const updated = { ...makeSourcePart('g1'), title: 'New' };
    const state = titleReducer(
      seeded,
      fetchGroupDetailsAndGenerateTitle.fulfilled(updated, 'req1', undefined as any)
    );
    expect(state.generatedGroupParts).toHaveLength(1);
    expect(state.generatedGroupParts[0].title).toBe('New');
  });

  it('sets error on rejected', () => {
    const state = titleReducer(
      initial,
      fetchGroupDetailsAndGenerateTitle.rejected(new Error('group fail'), 'req1', undefined as any)
    );
    expect(state.isGeneratingGroupTitle).toBe(false);
    expect(state.error).toBe('group fail');
  });
});
