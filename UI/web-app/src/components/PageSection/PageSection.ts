// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { PageSectionBase } from './PageSection.base';
import { getStyles } from './PageSection.styles';
import {
  type IPageSectionProps,
  type IPageSectionStyleProps,
  type IPageSectionStyles,
} from './PageSection.types';

export const PageSection: React.FunctionComponent<IPageSectionProps> = styled<
  IPageSectionProps,
  IPageSectionStyleProps,
  IPageSectionStyles
>(PageSectionBase, getStyles, undefined, {
  scope: 'PageSection',
});
