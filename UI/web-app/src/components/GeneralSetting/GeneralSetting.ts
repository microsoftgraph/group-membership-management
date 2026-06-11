// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { GeneralSettingBase } from './GeneralSetting.base';
import { getStyles } from './GeneralSetting.styles';
import {
  type GeneralSettingProps,
  type GeneralSettingStyleProps,
  type GeneralSettingStyles,
} from './GeneralSetting.types';

export const GeneralSetting: React.FunctionComponent<GeneralSettingProps> = styled<
  GeneralSettingProps,
  GeneralSettingStyleProps,
  GeneralSettingStyles
>(GeneralSettingBase, getStyles, undefined, {
  scope: 'GeneralSetting',
});