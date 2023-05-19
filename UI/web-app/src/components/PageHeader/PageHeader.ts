// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import * as React from 'react';
import { styled } from '@fluentui/react';
import { PageHeaderBase } from './PageHeader.base';
import { IPageHeaderProps, IPageHeaderStyleProps, IPageHeaderStyles } from './PageHeader.types';
import { getStyles } from './PageHeader.styles';

export const PageHeader: React.FunctionComponent<IPageHeaderProps> = styled<IPageHeaderProps, IPageHeaderStyleProps, IPageHeaderStyles>(
  PageHeaderBase,
  getStyles,
  undefined,
  {
    scope: 'PageHeader'
  }
);
