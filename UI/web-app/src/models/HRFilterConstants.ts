// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

// Sentinel token inserted into an HR filter string for an AND/OR operator that
// the user has not yet chosen. A filter containing this token is considered
// incomplete/invalid. Shared so the HR filter editor and the redux selectors
// agree on how a "missing AND/OR operator" is detected.
export const PLACEHOLDER_OPERATOR = 'placeholder';
