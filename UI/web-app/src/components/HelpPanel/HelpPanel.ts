// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { HelpPanelBase } from './HelpPanel.base';
import { getStyles } from './HelpPanel.styles';
import {
  type IHelpPanelProps,
  type IHelpPanelStyleProps,
  type IHelpPanelStyles,
} from './HelpPanel.types';

export const HelpPanel: React.FunctionComponent<IHelpPanelProps> = styled<
  IHelpPanelProps,
  IHelpPanelStyleProps,
  IHelpPanelStyles
>(HelpPanelBase, getStyles, undefined, {
  scope: 'HelpPanel',
});
