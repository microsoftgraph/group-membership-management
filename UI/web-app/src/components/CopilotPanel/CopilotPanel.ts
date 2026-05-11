// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { CopilotPanelBase } from './CopilotPanel.base';
import { getStyles } from './CopilotPanel.styles';
import {
    type ICopilotPanelProps,
    type ICopilotPanelStyleProps,
    type ICopilotPanelStyles,
} from './CopilotPanel.types';

export const CopilotPanel: React.FunctionComponent<ICopilotPanelProps> = styled<
    ICopilotPanelProps,
    ICopilotPanelStyleProps,
    ICopilotPanelStyles
>(CopilotPanelBase, getStyles, undefined, {
    scope: 'CopilotPanel',
});
