import * as React from 'react';
import { styled } from '@fluentui/react';
import { JobsListBase } from './JobsList.base';
import { IJobsListProps, IJobsListStyleProps, IJobsListStyles } from './JobsList.types';
import { getStyles } from './JobsList.styles';

export const JobsList: React.FunctionComponent<IJobsListProps> = styled<IJobsListProps, IJobsListStyleProps, IJobsListStyles>(
  JobsListBase,
  getStyles,
  undefined,
  {
    scope: 'JobsList'
  }
);
