// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { AdminConfigBase } from './AdminConfig.base';
import { getStyles } from './AdminConfig.styles';
import {
  type IAdminConfigProps,
  type IAdminConfigStyleProps,
  type IAdminConfigStyles,
} from './AdminConfig.types';

export const AdminConfig: React.FunctionComponent<IAdminConfigProps> = styled<
  IAdminConfigProps,
  IAdminConfigStyleProps,
  IAdminConfigStyles
>(AdminConfigBase, getStyles, undefined, {
  scope: 'AdminConfig',
});
