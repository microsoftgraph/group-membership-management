// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { describe, expect, it } from 'vitest';
import { isSourcePartValid, removeUnusedProperties } from './sourcePartUtils';
import { SourcePartType } from '../models/SourcePartType';
import type { ISourcePart } from '../models/ISourcePart';

const makePart = (type: SourcePartType, source: any, title = 'T'): ISourcePart => ({
  id: 'p1',
  title,
  isNew: false,
  isExpanded: false,
  query: { type, source } as any,
});

describe('isSourcePartValid', () => {
  describe('GroupMembership', () => {
    it('returns true for valid GUID', () => {
      expect(isSourcePartValid(makePart(SourcePartType.GroupMembership, '12345678-1234-1234-1234-123456789abc'))).toBe(true);
    });

    it('returns false for invalid GUID', () => {
      expect(isSourcePartValid(makePart(SourcePartType.GroupMembership, 'not-a-guid'))).toBe(false);
    });

    it('returns false for empty string', () => {
      expect(isSourcePartValid(makePart(SourcePartType.GroupMembership, ''))).toBe(false);
    });
  });

  describe('HR', () => {
    it('returns true with a valid manager id', () => {
      expect(isSourcePartValid(makePart(SourcePartType.HR, { manager: { id: 42 } }))).toBe(true);
    });

    it('returns true with a valid filter', () => {
      expect(isSourcePartValid(makePart(SourcePartType.HR, { filter: "dept = 'Sales'" }))).toBe(true);
    });

    it('returns false with empty filter and no manager', () => {
      expect(isSourcePartValid(makePart(SourcePartType.HR, { filter: '' }))).toBe(false);
    });

    it('returns false with whitespace-only filter', () => {
      expect(isSourcePartValid(makePart(SourcePartType.HR, { filter: '   ' }))).toBe(false);
    });

    it('returns false with undefined manager id', () => {
      expect(isSourcePartValid(makePart(SourcePartType.HR, { manager: { id: undefined } }))).toBe(false);
    });

    it('returns true with both manager and filter', () => {
      expect(isSourcePartValid(makePart(SourcePartType.HR, { manager: { id: 1 }, filter: "dept = 'Eng'" }))).toBe(true);
    });

    it('returns false when filter is missing equality operator', () => {
      expect(isSourcePartValid(makePart(SourcePartType.HR, { filter: "EmployeeType_Code 'FTE' 'Intern'" }))).toBe(false);
    });

    it('returns false when filter has attribute and value but no operator', () => {
      expect(isSourcePartValid(makePart(SourcePartType.HR, { manager: { id: 1 }, filter: "dept Sales" }))).toBe(false);
    });
  });

  describe('GroupOwnership', () => {
    it('returns true with non-empty source array', () => {
      expect(isSourcePartValid(makePart(SourcePartType.GroupOwnership, ['g1', 'g2']))).toBe(true);
    });

    it('returns false with empty source array', () => {
      expect(isSourcePartValid(makePart(SourcePartType.GroupOwnership, []))).toBe(false);
    });
  });

  describe('PlaceMembership', () => {
    it('returns true with non-empty source', () => {
      expect(isSourcePartValid(makePart(SourcePartType.PlaceMembership, 'place-filter'))).toBe(true);
    });

    it('returns false with empty source', () => {
      expect(isSourcePartValid(makePart(SourcePartType.PlaceMembership, ''))).toBe(false);
    });
  });

  it('returns false for unknown type', () => {
    expect(isSourcePartValid(makePart('Unknown' as any, 'x'))).toBe(false);
  });
});

describe('removeUnusedProperties', () => {
  it('returns GroupMembership query unchanged', () => {
    const query = { type: SourcePartType.GroupMembership, source: 'guid' } as any;
    expect(removeUnusedProperties(query)).toEqual(query);
  });

  it('returns GroupOwnership query unchanged', () => {
    const query = { type: SourcePartType.GroupOwnership, source: ['g1'] } as any;
    expect(removeUnusedProperties(query)).toEqual(query);
  });

  it('returns PlaceMembership query unchanged', () => {
    const query = { type: SourcePartType.PlaceMembership, source: 'filter' } as any;
    expect(removeUnusedProperties(query)).toEqual(query);
  });

  it('trims HR query with undefined manager', () => {
    const query = { type: SourcePartType.HR, source: { manager: undefined, filter: 'x' } } as any;
    const result = removeUnusedProperties(query);
    expect(result.source.filter).toBe('x');
    expect(result.source.manager).toBeUndefined();
  });

  it('preserves HR query with valid manager', () => {
    const query = { type: SourcePartType.HR, source: { manager: { id: 42, depth: 3 }, filter: 'x' } } as any;
    const result = removeUnusedProperties(query);
    expect(result.source.manager.id).toBe(42);
    expect(result.source.manager.depth).toBe(3);
  });

  it('handles HR query with manager having undefined id', () => {
    const query = { type: SourcePartType.HR, source: { manager: { id: undefined }, filter: 'f' } } as any;
    const result = removeUnusedProperties(query);
    expect(result.source.manager).toBeUndefined();
    expect(result.source.filter).toBe('f');
  });

  it('handles unsupported source part type gracefully', () => {
    const query = { type: 'Unknown', source: 'x' } as any;
    const result = removeUnusedProperties(query);
    expect(result).toEqual(query);
  });
});
