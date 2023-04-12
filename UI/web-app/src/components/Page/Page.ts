// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import * as React from 'react';
import { styled } from '@fluentui/react';
import { PageBase } from './Page.base';
import { IPageProps, IPageStyleProps, IPageStyles } from './Page.types';
import { getStyles } from './Page.styles';

export const Page: React.FunctionComponent<IPageProps> = styled<IPageProps, IPageStyleProps, IPageStyles>(
  PageBase,
  getStyles,
  undefined,
  {
    scope: 'Page'
  }
);