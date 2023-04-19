// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import * as React from 'react';
import { styled } from '@fluentui/react';
import { OwnerBase } from './Owner.base';
import { IOwnerProps, IOwnerStyleProps, IOwnerStyles } from './Owner.types';
import { getStyles } from './Owner.styles';

export const Owner: React.FunctionComponent<IOwnerProps> = styled<IOwnerProps, IOwnerStyleProps, IOwnerStyles>(
  OwnerBase,
  getStyles,
  undefined,
  {
    scope: 'Owner'
  }
);