// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { PageFooterBase } from './PageFooter.base';
import { getStyles } from './PageFooter.styles';
import {
  type IPageFooterProps,
  type IPageFooterStyleProps,
  type IPageFooterStyles,
} from './PageFooter.types';

export const PageFooter: React.FunctionComponent<IPageFooterProps> = styled<
  IPageFooterProps,
  IPageFooterStyleProps,
  IPageFooterStyles
>(PageFooterBase, getStyles, undefined, {
  scope: 'PageFooter',
});
