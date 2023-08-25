// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
  type IPageHeaderStyleProps,
  type IPageHeaderStyles,
} from './PageHeader.types';

export const getStyles = (props: IPageHeaderStyleProps): IPageHeaderStyles => {
  const { className, theme } = props;

  return {
    root: [{
    }, className],
    backButton: {
      padding: '7px 0px 0px 36px',
      display: 'flex',
      alignItems: 'center',
      gap: '8px',
      flexShrink: 0
    },
    separator: {
      width: '95%',
      height: '1px',
      background: '#D1D1D1',
      alignSelf: 'center'
    }
  };
};
