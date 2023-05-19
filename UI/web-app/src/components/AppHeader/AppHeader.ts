// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import * as React from 'react';
import { styled } from '@fluentui/react';
import { AppHeaderBase } from './AppHeader.base';
import { IAppHeaderProps, IAppHeaderStyleProps, IAppHeaderStyles } from './AppHeader.types';
import { getStyles } from './AppHeader.styles';

export const AppHeader: React.FunctionComponent<IAppHeaderProps> = styled<IAppHeaderProps, IAppHeaderStyleProps, IAppHeaderStyles>(
  AppHeaderBase,
  getStyles,
  undefined,
  {
    scope: 'AppHeader'
  }
);
