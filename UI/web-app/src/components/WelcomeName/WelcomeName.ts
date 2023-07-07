// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { WelcomeNameBase } from './WelcomeName.base';
import { getStyles } from './WelcomeName.styles';
import {
  type IWelcomeNameProps,
  type IWelcomeNameStyleProps,
  type IWelcomeNameStyles,
} from './WelcomeName.types';

export const WelcomeName: React.FunctionComponent<IWelcomeNameProps> = styled<
  IWelcomeNameProps,
  IWelcomeNameStyleProps,
  IWelcomeNameStyles
>(WelcomeNameBase, getStyles, undefined, {
  scope: 'WelcomeName',
});
