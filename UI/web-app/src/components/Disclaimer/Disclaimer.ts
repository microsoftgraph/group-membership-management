// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import { DisclaimerBase } from './Disclaimer.base';
import { getStyles } from './Disclaimer.styles';
import { IDisclaimerProps, IDisclaimerStyleProps, IDisclaimerStyles } from './Disclaimer.types';

export const Disclaimer = styled<IDisclaimerProps, IDisclaimerStyleProps, IDisclaimerStyles>(
    DisclaimerBase,
    getStyles
);