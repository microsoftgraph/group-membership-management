// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
  type IJobsListStyleProps,
  type IJobsListStyles,
} from './JobsList.types';

export const getStyles = (props: IJobsListStyleProps): IJobsListStyles => {
  const { className, theme } = props;

  return {
    root: [{}, className],
    enabled: {
      color: theme.palette.black,
      backgroundColor: theme.semanticColors.successBackground,
      borderRadius: 50,
      textAlign: 'center',
      height: 20,
    },
    disabled: {
      color: theme.palette.black,
      backgroundColor: theme.palette.themeLighterAlt,
      borderRadius: 50,
      textAlign: 'center',
      height: 20,
    },
    actionRequired: {
      color: theme.semanticColors.errorIcon,
    },
    title: {
      paddingLeft: 12
    },
    tabContent: {
      cursor: 'pointer',
      display: 'flex',
      flexDirection: 'column'
    },
    columnToEnd: {
      flexGrow: 1
    },
    refresh: {
      padding: 22.5,
    },
  };
};
