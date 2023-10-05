// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { FontWeights } from '@fluentui/react';
import {
  type IErrorStyleProps,
  type IErrorStyles,
} from './Error.types';

export const getStyles = (props: IErrorStyleProps): IErrorStyles => {
  const { className, theme} = props;

  return {
    root: [{
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      height: '100vh'
    }, className],
    text: {
      ...theme.fonts.xLarge,
      fontWeight: FontWeights.semibold,
      lineHeight: 40
    },
  };
};
