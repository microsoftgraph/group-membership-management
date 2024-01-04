// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type IPageStyleProps, type IPageStyles } from './Page.types';

export const getStyles = (props: IPageStyleProps): IPageStyles => {
  const { className } = props;

  return {
    root: [{}, className],
    privacyPolicy: {
      position: 'absolute',
      bottom: 16,
      left: '50%',
      transform: 'translateX(-50%)',
    }
  };
};
