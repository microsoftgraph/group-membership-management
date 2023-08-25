// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
  type IJobsListStyleProps,
  type IJobsListStyles,
} from './JobsList.types';

export const getStyles = (props: IJobsListStyleProps): IJobsListStyles => {
  const { className, theme } = props;

  return {
    root: [{
      margin: '0px 36px 12px 36px'
    }, className],
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
    jobsList: {
      backgroundColor: theme.palette.white,
      borderRadius: 10,
      padding: '12px 0px 12px 24px',
    }
  };
};
