// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { HyperlinkContainerBase } from './HyperlinkContainer.base';
import { getStyles } from './HyperlinkContainer.styles';
import {
  type IHyperlinkContainerProps,
  type IHyperlinkContainerStyleProps,
  type IHyperlinkContainerStyles,
} from './HyperlinkContainer.types';

export const HyperlinkContainer: React.FunctionComponent<IHyperlinkContainerProps> = styled<
  IHyperlinkContainerProps,
  IHyperlinkContainerStyleProps,
  IHyperlinkContainerStyles
>(HyperlinkContainerBase, getStyles, undefined, {
  scope: 'HyperlinkContainer',
});
