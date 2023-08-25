// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { JobDetailsBase } from './JobDetails.base';
import { getStyles } from './JobDetails.styles';
import {
  type IJobDetailsProps,
  type IJobDetailsStyleProps,
  type IJobDetailsStyles,
} from './JobDetails.types';

export const JobDetails: React.FunctionComponent<IJobDetailsProps> = styled<
  IJobDetailsProps,
  IJobDetailsStyleProps,
  IJobDetailsStyles
>(JobDetailsBase, getStyles, undefined, {
  scope: 'JobDetails',
});
