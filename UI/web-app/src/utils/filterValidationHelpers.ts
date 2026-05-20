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

const VALID_EQUALITY_OPERATORS = ["NOT IN", "<=", ">=", "<>", "=", ">", "<", "IS", "IN"];

/**
 * Checks if every segment of a filter string contains a valid equality operator.
 * A segment is a clause separated by AND/OR. Each segment must have one of the
 * recognized operators (=, <, <=, >, >=, <>, IS, IN, NOT IN).
 * @param filter The filter string to validate
 * @returns True if all segments have valid equality operators, false otherwise
 */
export function hasValidEqualityOperators(filter: string): boolean {
  if (!filter || filter.trim() === '') {
    return false;
  }

  // Remove trailing AND/OR before validating
  let cleanedFilter = filter.trim();
  const trailingAndOrPattern = /\s+(and|or)\s*$/i;
  cleanedFilter = cleanedFilter.replace(trailingAndOrPattern, '');

  if (cleanedFilter.trim() === '') {
    return false;
  }

  // Split the filter into segments by AND/OR (respecting parentheses)
  // Simple approach: split on AND/OR that are not inside parentheses or quotes
  const segments = splitFilterSegments(cleanedFilter);

  // Each segment must contain a valid equality operator
  const sortedOperators = [...VALID_EQUALITY_OPERATORS].sort((a, b) => b.length - a.length);

  return segments.every(segment => {
    const trimmed = segment.trim();
    if (trimmed === '') return true; // skip empty segments from trailing splits

    // Check if the segment contains at least one valid equality operator surrounded by spaces
    return sortedOperators.some(op => {
      const regex = new RegExp(`\\s+${op.replace(/\s+/g, '\\s+')}\\s+`, 'i');
      return regex.test(` ${trimmed} `); // pad with spaces to match operators at start/end
    });
  });
}

/**
 * Splits a filter string into individual clause segments by AND/OR operators
 * that are not inside parentheses or quotes.
 */
function splitFilterSegments(filter: string): string[] {
  const segments: string[] = [];
  let current = '';
  let depth = 0;
  let inQuote = false;

  for (let i = 0; i < filter.length; i++) {
    const char = filter[i];

    if (char === "'" && !inQuote) {
      inQuote = true;
      current += char;
    } else if (char === "'" && inQuote) {
      inQuote = false;
      current += char;
    } else if (char === '(' && !inQuote) {
      depth++;
      current += char;
    } else if (char === ')' && !inQuote) {
      depth--;
      current += char;
    } else if (depth === 0 && !inQuote) {
      // Check for AND or OR at this position
      const remaining = filter.substring(i);
      const andMatch = remaining.match(/^(\s+and\s+)/i);
      const orMatch = remaining.match(/^(\s+or\s+)/i);

      if (andMatch) {
        segments.push(current);
        current = '';
        i += andMatch[1].length - 1;
      } else if (orMatch) {
        segments.push(current);
        current = '';
        i += orMatch[1].length - 1;
      } else {
        current += char;
      }
    } else {
      current += char;
    }
  }

  if (current.trim()) {
    segments.push(current);
  }

  return segments;
}
