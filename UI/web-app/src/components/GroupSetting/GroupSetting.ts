// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { GroupSettingBase } from './GroupSetting.base';
import { getStyles } from './GroupSetting.styles';
import { type IGroupSettingProps, type IGroupSettingStyleProps, type IGroupSettingStyles } from './GroupSetting.types';

export const GroupSetting: React.FunctionComponent<IGroupSettingProps> = styled<
    IGroupSettingProps,
    IGroupSettingStyleProps,
    IGroupSettingStyles
>(GroupSettingBase, getStyles, undefined, {
    scope: 'GroupSetting',
});
