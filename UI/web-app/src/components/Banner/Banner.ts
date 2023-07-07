// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { BannerBase } from './Banner.base';
import { getStyles } from './Banner.styles';
import {
  type IBannerProps,
  type IBannerStyleProps,
  type IBannerStyles,
} from './Banner.types';

export const Banner: React.FunctionComponent<IBannerProps> = styled<
  IBannerProps,
  IBannerStyleProps,
  IBannerStyles
>(BannerBase, getStyles, undefined, {
  scope: 'Banner',
});
