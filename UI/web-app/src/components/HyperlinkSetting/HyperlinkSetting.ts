// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { HyperlinkSettingBase } from './HyperlinkSetting.base';
import { getStyles } from './HyperlinkSetting.styles';
import {
  type HyperlinkSettingProps,
  type HyperlinkSettingStyleProps,
  type HyperlinkSettingStyles,
} from './HyperlinkSetting.types';

export const HyperlinkSetting: React.FunctionComponent<HyperlinkSettingProps> = styled<
  HyperlinkSettingProps,
  HyperlinkSettingStyleProps,
  HyperlinkSettingStyles
>(HyperlinkSettingBase, getStyles, undefined, {
  scope: 'HyperlinkSetting',
});
