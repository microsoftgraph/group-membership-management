// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import type { AdvancedViewToggleStyleProps, AdvancedViewToggleStyles } from './AdvancedViewToggle.types';

export const getStyles = (props: AdvancedViewToggleStyleProps): AdvancedViewToggleStyles => {
  const { className } = props;

  return {
    root: [{
      display: 'flex',
      alignItems: 'center',
    }, className],
  };
};
