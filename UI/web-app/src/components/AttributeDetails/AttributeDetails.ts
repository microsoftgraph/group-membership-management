// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { AttributeDetailsBase } from './AttributeDetails.base';
import { getStyles } from './AttributeDetails.styles';
import {
  type AttributeDetailsProps,
  type AttributeDetailsStyleProps,
  type AttributeDetailsStyles,
} from './AttributeDetails.types';

export const AttributeDetails: React.FunctionComponent<AttributeDetailsProps> = styled<
  AttributeDetailsProps,
  AttributeDetailsStyleProps,
  AttributeDetailsStyles
>(AttributeDetailsBase, getStyles, undefined, {
  scope: 'AttributeDetails',
});