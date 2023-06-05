// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
  type IPageHeaderStyleProps,
  type IPageHeaderStyles,
} from './PageHeader.types';

export const getStyles = (props: IPageHeaderStyleProps): IPageHeaderStyles => {
  const { className } = props;

  return {
    root: [{}, className],
  };
};
