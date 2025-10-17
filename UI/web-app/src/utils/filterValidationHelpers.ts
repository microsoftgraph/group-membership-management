// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

/**
 * Checks if a filter string has a trailing AND/OR operator
 * @param filter The filter string to check
 * @returns True if the filter has a trailing AND/OR operator, false otherwise
 */
export function hasTrailingAndOrOperator(filter: string): boolean {
  if (!filter || filter.trim() === '') {
    return false;
  }

  // Remove any trailing whitespace
  const trimmedFilter = filter.trim();
  
  // Check if the filter ends with AND or OR (case insensitive)
  const trailingAndOrPattern = /\s+(and|or)\s*$/i;
  
  return trailingAndOrPattern.test(trimmedFilter);
}

/**
 * Removes trailing AND/OR operators from a filter string
 * @param filter The filter string to clean
 * @returns The filter string with trailing AND/OR operators removed
 */
export function removeTrailingAndOrOperator(filter: string): string {
  if (!filter || filter.trim() === '') {
    return filter;
  }

  // Remove trailing AND, OR operators (case insensitive)
  // This pattern matches whitespace + (and|or) + optional whitespace at the end
  const trailingAndOrPattern = /\s+(and|or)\s*$/i;
  
  return filter.replace(trailingAndOrPattern, '');
};