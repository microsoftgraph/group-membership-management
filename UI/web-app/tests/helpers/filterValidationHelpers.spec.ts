// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { test, expect, Page } from '@playwright/test';
import { removeUnusedProperties } from '../../src/utils/sourcePartUtils';
import { SourcePartType } from '../../src/models/SourcePartType';
import { HRSourcePart } from '../../src/models/HRSourcePart';

test.use({ storageState: 'tests/storageState.json' });

// Configure retries for filter validation tests since they involve complex UI interactions
test.describe('Filter Validation Tests', () => {
  test.describe.configure({ retries: 2 });

  test('Test removeUnusedProperties function with trailing AND/OR operator removal', async () => {
    console.log('Testing removeUnusedProperties function directly');

    // Create a mock HR source part with trailing AND operator
    const mockHRSourcePartWithTrailingAnd: HRSourcePart = {
      type: SourcePartType.HR,
      source: {
        filter: 'EmployeeType_Code IN (\'FTE\', \'Intern\') AND',
        manager: {
          id: 12345,
          depth: 2
        }
      }
    };

    // Create a mock HR source part with trailing OR operator
    const mockHRSourcePartWithTrailingOr: HRSourcePart = {
      type: SourcePartType.HR,
      source: {
        filter: 'Department_Code = \'IT\' OR',
        manager: undefined
      }
    };

    // Create a mock HR source part with valid filter (no trailing operators)
    const mockHRSourcePartValid: HRSourcePart = {
      type: SourcePartType.HR,
      source: {
        filter: 'EmployeeType_Code IN (\'FTE\', \'Intern\') AND Department_Code = \'IT\'',
        manager: {
          id: 12345,
          depth: 2
        }
      }
    };

    // Test trailing AND removal
    const cleanedAnd = removeUnusedProperties(mockHRSourcePartWithTrailingAnd);
    const andSource = cleanedAnd.source as { filter?: string; manager?: { id?: number; depth?: number } };
    expect(andSource.filter).toBe('EmployeeType_Code IN (\'FTE\', \'Intern\')');
    expect(andSource.filter).not.toMatch(/\s+(and|or)\s*$/i);
    console.log('✅ Trailing AND operator removed successfully');

    // Test trailing OR removal
    const cleanedOr = removeUnusedProperties(mockHRSourcePartWithTrailingOr);
    const orSource = cleanedOr.source as { filter?: string; manager?: { id?: number; depth?: number } };
    expect(orSource.filter).toBe('Department_Code = \'IT\'');
    expect(orSource.filter).not.toMatch(/\s+(and|or)\s*$/i);
    console.log('✅ Trailing OR operator removed successfully');

    // Test valid filter remains unchanged
    const cleanedValid = removeUnusedProperties(mockHRSourcePartValid);
    const validSource = cleanedValid.source as { filter?: string; manager?: { id?: number; depth?: number } };
    expect(validSource.filter).toBe('EmployeeType_Code IN (\'FTE\', \'Intern\') AND Department_Code = \'IT\'');
    expect(validSource.filter).not.toMatch(/\s+(and|or)\s*$/i);
    console.log('✅ Valid filter remains unchanged');

    // Test manager properties are preserved
    expect(andSource.manager?.id).toBe(12345);
    expect(andSource.manager?.depth).toBe(2);
    expect(validSource.manager?.id).toBe(12345);
    expect(validSource.manager?.depth).toBe(2);
    console.log('✅ Manager properties preserved correctly');

    console.log('✅ removeUnusedProperties function tests completed successfully');
  });
});