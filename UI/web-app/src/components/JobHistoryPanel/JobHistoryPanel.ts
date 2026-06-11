// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { JobHistoryPanelBase } from './JobHistoryPanel.base';
import { getStyles } from './JobHistoryPanel.styles';
import {
  type IJobHistoryPanelProps,
  type IJobHistoryPanelStyleProps,
  type IJobHistoryPanelStyles,
} from './JobHistoryPanel.types';

export const JobHistoryPanel: React.FunctionComponent<IJobHistoryPanelProps> = styled<
  IJobHistoryPanelProps,
  IJobHistoryPanelStyleProps,
  IJobHistoryPanelStyles
>(JobHistoryPanelBase, getStyles, undefined, {
  scope: 'JobHistoryPanel',
});
