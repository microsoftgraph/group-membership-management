// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react'
import type * as React from 'react'

import { PageBase } from './Page.base'
import { getStyles } from './Page.styles'
import { type IPageProps, type IPageStyleProps, type IPageStyles } from './Page.types'

export const Page: React.FunctionComponent<IPageProps> = styled<IPageProps, IPageStyleProps, IPageStyles>(
  PageBase,
  getStyles,
  undefined,
  {
    scope: 'Page'
  }
)
