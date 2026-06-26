// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { describe, expect, it } from 'vitest';
import { allSourcePartsHaveFreshTitles } from './titleFreshness';
import { SourcePartType } from '../models/SourcePartType';
import type { ISourcePart } from '../models/ISourcePart';
import type { HRPart } from '../models/HRPart';

const makeHRPart = (id: string, filter: string, title = 'Some title'): ISourcePart => ({
  id,
  title,
  isNew: false,
  isExpanded: false,
  query: {
    type: SourcePartType.HR,
    source: { filter },
  } as any,
});

const makeGroupPart = (id: string, title = 'Group title'): ISourcePart => ({
  id,
  title,
  isNew: false,
  isExpanded: false,
  query: {
    type: SourcePartType.GroupMembership,
  } as any,
});

const makeAITitle = (partId: string, filter: string, title = 'AI title'): HRPart => ({
  partId,
  filter,
  title,
});

describe('allSourcePartsHaveFreshTitles', () => {
  it('returns true when there are no source parts', () => {
    expect(allSourcePartsHaveFreshTitles([], [])).toBe(true);
  });

  it('returns true when all parts are non-HR with non-empty titles', () => {
    const parts = [makeGroupPart('g1'), makeGroupPart('g2')];
    expect(allSourcePartsHaveFreshTitles(parts, [])).toBe(true);
  });

  it('returns false when a non-HR part has an empty title', () => {
    const parts = [makeGroupPart('g1', '')];
    expect(allSourcePartsHaveFreshTitles(parts, [])).toBe(false);
  });

  it('returns false when a non-HR part has a whitespace-only title', () => {
    const parts = [makeGroupPart('g1', '   ')];
    expect(allSourcePartsHaveFreshTitles(parts, [])).toBe(false);
  });

  it('returns true when HR part has a fresh AI title from aiGeneratedTitles', () => {
    const parts = [makeHRPart('h1', 'dept eq Sales')];
    const aiTitles = [makeAITitle('h1', 'dept eq Sales')];
    expect(allSourcePartsHaveFreshTitles(parts, aiTitles)).toBe(true);
  });

  it('returns false when HR part has a stale AI title (filter changed)', () => {
    const parts = [makeHRPart('h1', 'dept eq Marketing')];
    const aiTitles = [makeAITitle('h1', 'dept eq Sales')];
    expect(allSourcePartsHaveFreshTitles(parts, aiTitles)).toBe(false);
  });

  it('returns false when HR part has no AI title at all', () => {
    const parts = [makeHRPart('h1', 'dept eq Sales')];
    expect(allSourcePartsHaveFreshTitles(parts, [])).toBe(false);
  });

  it('returns true with mixed HR and non-HR parts, all valid', () => {
    const parts = [
      makeGroupPart('g1'),
      makeHRPart('h1', 'filter1'),
      makeHRPart('h2', 'filter2'),
    ];
    const aiTitles = [
      makeAITitle('h1', 'filter1'),
      makeAITitle('h2', 'filter2'),
    ];
    expect(allSourcePartsHaveFreshTitles(parts, aiTitles)).toBe(true);
  });

  it('returns false when one of multiple HR parts has a stale title', () => {
    const parts = [
      makeHRPart('h1', 'filter1'),
      makeHRPart('h2', 'filter2-changed'),
    ];
    const aiTitles = [
      makeAITitle('h1', 'filter1'),
      makeAITitle('h2', 'filter2'),
    ];
    expect(allSourcePartsHaveFreshTitles(parts, aiTitles)).toBe(false);
  });

  it('returns false when HR part title is empty even if AI title is fresh', () => {
    const parts = [makeHRPart('h1', 'filter1', '')];
    const aiTitles = [makeAITitle('h1', 'filter1')];
    expect(allSourcePartsHaveFreshTitles(parts, aiTitles)).toBe(false);
  });

  it('handles HR parts without a filter (treated as non-HR for freshness)', () => {
    const part: ISourcePart = {
      id: 'h1',
      title: 'Title',
      isNew: false,
      isExpanded: false,
      query: { type: SourcePartType.HR, source: {} } as any,
    };
    expect(allSourcePartsHaveFreshTitles([part], [])).toBe(true);
  });
});
