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
    }, className],
  };
};
