// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { AppHeaderBase } from './AppHeader.base';
import { getStyles } from './AppHeader.styles';
import {
  type IAppHeaderProps,
  type IAppHeaderStyleProps,
  type IAppHeaderStyles,
} from './AppHeader.types';

export const AppHeader: React.FunctionComponent<IAppHeaderProps> = styled<
  IAppHeaderProps,
  IAppHeaderStyleProps,
  IAppHeaderStyles
>(AppHeaderBase, getStyles, undefined, {
  scope: 'AppHeader',
});
