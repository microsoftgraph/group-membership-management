// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { ErrorBase } from './Error.base';
import { getStyles } from './Error.styles';
import {
  type IErrorProps,
  type IErrorStyleProps,
  type IErrorStyles,
} from './Error.types';

export const Error: React.FunctionComponent<IErrorProps> = styled<
  IErrorProps,
  IErrorStyleProps,
  IErrorStyles
>(ErrorBase, getStyles, undefined, {
  scope: 'Error',
});
