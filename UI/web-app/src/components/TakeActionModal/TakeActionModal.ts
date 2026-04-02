// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { TakeActionModalBase } from './TakeActionModal.base';
import { getStyles } from './TakeActionModal.styles';
import type {
    ITakeActionModalProps,
    ITakeActionModalStyleProps,
    ITakeActionModalStyles,
} from './TakeActionModal.types';

export const TakeActionModal: React.FunctionComponent<ITakeActionModalProps> = styled<
    ITakeActionModalProps,
    ITakeActionModalStyleProps,
    ITakeActionModalStyles
>(TakeActionModalBase, getStyles, undefined, {
    scope: 'TakeActionModal',
});
