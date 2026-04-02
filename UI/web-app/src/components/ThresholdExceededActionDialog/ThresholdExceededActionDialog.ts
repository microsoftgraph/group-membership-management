// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { ThresholdExceededActionDialogBase } from './ThresholdExceededActionDialog.base';
import { getStyles } from './ThresholdExceededActionDialog.styles';
import type {
    IThresholdExceededActionDialogProps,
    IThresholdExceededActionDialogStyleProps,
    IThresholdExceededActionDialogStyles,
} from './ThresholdExceededActionDialog.types';

export const ThresholdExceededActionDialog: React.FunctionComponent<IThresholdExceededActionDialogProps> = styled<
    IThresholdExceededActionDialogProps,
    IThresholdExceededActionDialogStyleProps,
    IThresholdExceededActionDialogStyles
>(ThresholdExceededActionDialogBase, getStyles, undefined, {
    scope: 'ThresholdExceededActionDialog',
});
