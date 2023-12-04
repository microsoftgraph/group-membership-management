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
    titleContainer: {
      display: 'flex',
      justifyContent: 'space-between',
    },
    title: {
      paddingLeft: 9
    },
    tabContent: {
      cursor: 'pointer',
      display: 'flex',
      flexDirection: 'column',
      paddingLeft: 6
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
      padding: '12px 24px 12px 24px',
    },
    jobsListFilter: {
      marginBottom: 12,
    },
    footer: {
      display: 'flex',
      justifyContent: 'space-between'
    },
    noMembershipsFoundText: {
      textAlign: 'center',
      padding: 24
    },
    errorMessageBar: {
      borderRadius: 5,
      marginBottom: 22,
    }

  };
};
