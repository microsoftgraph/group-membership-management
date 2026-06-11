// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { MaintenanceBase } from './Maintenance.base';
import { getStyles } from './Maintenance.styles';
import {
  type IMaintenanceProps,
  type IMaintenanceStyleProps,
  type IMaintenanceStyles,
} from './Maintenance.types';

export const Maintenance: React.FunctionComponent<IMaintenanceProps> = styled<
  IMaintenanceProps,
  IMaintenanceStyleProps,
  IMaintenanceStyles
>(MaintenanceBase, getStyles, undefined, {
  scope: 'Maintenance',
});
