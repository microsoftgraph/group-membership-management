// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { ContentContainerBase } from './ContentContainer.base';
import { getStyles } from './ContentContainer.styles';
import {
  type IContentContainerProps,
  type IContentContainerStyleProps,
  type IContentContainerStyles,
} from './ContentContainer.types';

export const ContentContainer: React.FunctionComponent<IContentContainerProps> = styled<
  IContentContainerProps,
  IContentContainerStyleProps,
  IContentContainerStyles
>(ContentContainerBase, getStyles, undefined, {
  scope: 'ContentContainer',
});
