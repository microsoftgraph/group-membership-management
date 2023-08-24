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
      padding: '12px 37px',
      justifyContent: 'space-between',
      alignItems: 'center',
      display: 'flex',
      width: '100%',
      backgroundColor: theme.palette.neutralLight,
    }, className],
  };
};
