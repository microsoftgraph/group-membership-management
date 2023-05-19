// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { IPageStyleProps, IPageStyles } from './Page.types';

export const getStyles = (props: IPageStyleProps): IPageStyles => {
  const { className, } = props;

  return {
    root: [
      { },
      className
    ]
  };
};