// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { CreateGroupBase } from './CreateGroup.base';
import { getStyles } from './CreateGroup.styles';
import { type ICreateGroupProps, type ICreateGroupStyleProps, type ICreateGroupStyles } from './CreateGroup.types';

export const CreateGroup: React.FunctionComponent<ICreateGroupProps> = styled<
  ICreateGroupProps,
  ICreateGroupStyleProps,
  ICreateGroupStyles
>(CreateGroupBase, getStyles, undefined, {
  scope: 'CreateGroup',
});
