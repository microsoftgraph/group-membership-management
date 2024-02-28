// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
  type IPageVersionStyleProps,
  type IPageVersionStyles,
} from './PageVersion.types';

export const getStyles = (props: IPageVersionStyleProps): IPageVersionStyles => {
  const { className, theme } = props;

  return {
    root: [{
      padding: '20px 36px',
      color: theme.palette.neutralSecondary,
      fontSize: 12,
      fontWeight: '400'
    }, className],
  };
};
