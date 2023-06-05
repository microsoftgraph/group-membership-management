// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react'
import type * as React from 'react'

import { PageHeaderBase } from './PageHeader.base'
import { getStyles } from './PageHeader.styles'
import { type IPageHeaderProps, type IPageHeaderStyleProps, type IPageHeaderStyles } from './PageHeader.types'

export const PageHeader: React.FunctionComponent<IPageHeaderProps> = styled<IPageHeaderProps, IPageHeaderStyleProps, IPageHeaderStyles>(
  PageHeaderBase,
  getStyles,
  undefined,
  {
    scope: 'PageHeader'
  }
)
