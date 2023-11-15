// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { AdminConfigBase } from './AdminConfig.base';
import { getStyles } from './AdminConfig.styles';
import {
  type AdminConfigProps,
  type AdminConfigStyleProps,
  type AdminConfigStyles,
} from './AdminConfig.types';

export const AdminConfig: React.FunctionComponent<AdminConfigProps> = styled<
  AdminConfigProps,
  AdminConfigStyleProps,
  AdminConfigStyles
>(AdminConfigBase, getStyles, undefined, {
  scope: 'AdminConfig',
});
