// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { ColumnActionsMode } from '@fluentui/react';
import { describe, expect, it } from 'vitest';

import { getDestinationTypeColumnActionsMode } from './JobsList.utils';

describe('getDestinationTypeColumnActionsMode', () => {
  it('enables the Type dropdown for tenant job writers', () => {
    expect(getDestinationTypeColumnActionsMode(true, false)).toBe(ColumnActionsMode.hasDropdown);
  });

  it('enables the Type dropdown for Teams Channel onboarders', () => {
    expect(getDestinationTypeColumnActionsMode(false, true)).toBe(ColumnActionsMode.hasDropdown);
  });

  it('keeps the Type header non-interactive without either role', () => {
    expect(getDestinationTypeColumnActionsMode(false, false)).toBe(ColumnActionsMode.disabled);
  });
});
