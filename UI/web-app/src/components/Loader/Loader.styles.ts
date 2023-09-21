// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { FontWeights } from '@fluentui/react';
import {
  type ILoaderStyleProps,
  type ILoaderStyles,
} from './Loader.types';

export const getStyles = (props: ILoaderStyleProps): ILoaderStyles => {
  const { className, theme} = props;

  return {
    root: [{
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      height: '100vh'
    }, className],
    spinner: {
      marginRight: '8px'
    },
    text: {
      ...theme.fonts.xLarge,
      fontWeight: FontWeights.semibold,
      lineHeight: 40
    },
  };
};
