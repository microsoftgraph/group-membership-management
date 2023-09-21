// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { LoaderBase } from './Loader.base';
import { getStyles } from './Loader.styles';
import {
  type ILoaderProps,
  type ILoaderStyleProps,
  type ILoaderStyles,
} from './Loader.types';

export const Loader: React.FunctionComponent<ILoaderProps> = styled<
  ILoaderProps,
  ILoaderStyleProps,
  ILoaderStyles
>(LoaderBase, getStyles, undefined, {
  scope: 'Loader',
});
