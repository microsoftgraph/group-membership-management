// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createTheme } from '@fluentui/react';
import { describe, expect, it } from 'vitest';

import { getStyles } from './JobsList.styles';

describe('JobsList styles', () => {
  it('shows the pointer cursor throughout clickable Dashboard rows', () => {
    const styles = getStyles({ theme: createTheme() });

    expect(styles.jobsList).toMatchObject({
      selectors: {
        '.ms-DetailsRow, .ms-DetailsRow *': {
          cursor: 'pointer',
        },
      },
    });
  });
});
