// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { AppBase } from './App.base';
import { getStyles } from './App.styles';
import {
  type IAppProps,
  type IAppStyleProps,
  type IAppStyles,
} from './App.types';

export const App: React.FunctionComponent<IAppProps> = styled<
  IAppProps,
  IAppStyleProps,
  IAppStyles
>(AppBase, getStyles, undefined, {
  scope: 'App',
});
