// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { JobsListFilterBase } from './JobsListFilter.base';
import { getStyles } from './JobsListFilter.styles';
import {
  type IJobsListFilterProps,
  type IJobsListFilterStyleProps,
  type IJobsListFilterStyles,
} from './JobsListFilter.types';

export const JobsListFilter: React.FunctionComponent<IJobsListFilterProps> = styled<
  IJobsListFilterProps,
  IJobsListFilterStyleProps,
  IJobsListFilterStyles
>(JobsListFilterBase, getStyles, undefined, {
  scope: 'JobsList',
});
