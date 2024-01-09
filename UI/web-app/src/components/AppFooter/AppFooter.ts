// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { AppFooterBase } from './AppFooter.base';
import { getStyles } from './AppFooter.styles';
import {
  type IAppFooterProps,
  type IAppFooterStyleProps,
  type IAppFooterStyles,
} from './AppFooter.types';

export const AppFooter: React.FunctionComponent<IAppFooterProps> = styled<
  IAppFooterProps,
  IAppFooterStyleProps,
  IAppFooterStyles
>(AppFooterBase, getStyles, undefined, {
  scope: 'AppFooter',
});
