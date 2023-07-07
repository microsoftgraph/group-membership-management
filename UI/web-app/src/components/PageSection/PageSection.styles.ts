// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
  type IPageSectionStyleProps,
  type IPageSectionStyles,
} from './PageSection.types';

export const getStyles = (props: IPageSectionStyleProps): IPageSectionStyles => {
  const { className, theme } = props;

  return {
    root: [{
      backgroundColor: theme.palette.white,
      borderRadius: 10,
      padding: 14,
      margin: '0px 24px 12px 24px'
    }, className],
  };
};
