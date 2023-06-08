// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { JobsListBase } from './JobsList.base';
import { getStyles } from './JobsList.styles';
import {
  type IJobsListProps,
  type IJobsListStyleProps,
  type IJobsListStyles,
} from './JobsList.types';

export const JobsList: React.FunctionComponent<IJobsListProps> = styled<
  IJobsListProps,
  IJobsListStyleProps,
  IJobsListStyles
>(JobsListBase, getStyles, undefined, {
  scope: 'JobsList',
});
