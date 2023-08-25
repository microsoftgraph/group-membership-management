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
      padding: '24px 0px 24px 36px',
      display: 'flex',
      alignItems: 'center',
      gap: '8px',
      flexShrink: 0
    },
    separator: {
      width: '95%',
      height: '1px',
      background: theme.palette.neutralQuaternaryAlt,
      alignSelf: 'center'
    }
  };
};
