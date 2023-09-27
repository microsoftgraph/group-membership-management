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
    actionButtonsContainer:{
      padding: '12px 36px 12px 36px',
    },
    backButton: {
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
