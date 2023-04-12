// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import * as React from 'react';
import { styled } from '@fluentui/react';
import { AppBase } from './App.base';
import { IAppProps, IAppStyleProps, IAppStyles } from './App.types';
import { getStyles } from './App.styles';

export const App: React.FunctionComponent<IAppProps> = styled<IAppProps, IAppStyleProps, IAppStyles>(
  AppBase,
  getStyles,
  undefined,
  {
    scope: 'App'
  }
);
