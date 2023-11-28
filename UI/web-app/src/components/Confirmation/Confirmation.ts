// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { ConfirmationBase } from './Confirmation.base';
import { getStyles } from './Confirmation.styles';
import {
  type IConfirmationProps,
  type IConfirmationStyleProps,
  type IConfirmationStyles,
} from './Confirmation.types';

export const Confirmation: React.FunctionComponent<IConfirmationProps> = styled<
  IConfirmationProps,
  IConfirmationStyleProps,
  IConfirmationStyles
>(ConfirmationBase, getStyles, undefined, {
  scope: 'Confirmation',
});
