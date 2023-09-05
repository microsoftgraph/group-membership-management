// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { PageVersionBase } from './PageVersion.base';
import { getStyles } from './PageVersion.styles';
import {
  type IPageVersionProps,
  type IPageVersionStyleProps,
  type IPageVersionStyles,
} from './PageVersion.types';

export const PageVersion: React.FunctionComponent<IPageVersionProps> = styled<
  IPageVersionProps,
  IPageVersionStyleProps,
  IPageVersionStyles
>(PageVersionBase, getStyles, undefined, {
  scope: 'PageVersion',
});
