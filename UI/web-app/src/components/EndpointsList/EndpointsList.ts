// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { EndpointsListBase } from './EndpointsList.base';
import { getStyles } from './EndpointsList.styles';
import {
  type IEndpointsListProps,
  type IEndpointsListStyleProps,
  type IEndpointsListStyles,
} from './EndpointsList.types';

export const EndpointsList: React.FunctionComponent<IEndpointsListProps> = styled<
  IEndpointsListProps,
  IEndpointsListStyleProps,
  IEndpointsListStyles
>(EndpointsListBase, getStyles, undefined, {
  scope: 'EndpointsList',
});
