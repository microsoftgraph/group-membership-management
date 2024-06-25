// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { NotFoundBase } from './NotFound.base';
import { getStyles } from './NotFound.styles';
import {
  type INotFoundProps,
  type INotFoundStyleProps,
  type INotFoundStyles,
} from './NotFound.types';

export const NotFound: React.FunctionComponent<INotFoundProps> = styled<
  INotFoundProps,
  INotFoundStyleProps,
  INotFoundStyles
>(NotFoundBase, getStyles, undefined, {
  scope: 'NotFound',
});
