// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { FontWeights } from '@fluentui/react';
import {
  type IWelcomeNameStyleProps,
  type IWelcomeNameStyles,
} from './WelcomeName.types';

export const getStyles = (props: IWelcomeNameStyleProps): IWelcomeNameStyles => {
  const { className, theme} = props;

  return {
    root: [{
      ...theme.fonts.xxLarge, // 28
      fontWeight: FontWeights.semibold, // 600
      lineHeight: 40,
    }, className],
  };
};
