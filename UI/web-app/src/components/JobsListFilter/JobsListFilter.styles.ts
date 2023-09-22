// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
  IJobsListFilterStyleProps,
  IJobsListFilterStyles,
} from './JobsListFilter.types';

export const getStyles = (props: IJobsListFilterStyleProps): IJobsListFilterStyles => {
  const { className, theme } = props;

  return {
    root: [{}, className],
    container: {
      backgroundColor: theme.palette.white,
      borderRadius: 10,
      padding: 14,
    },
    filterButton: {
      borderRadius: 4,
      border: '1px solid',
      borderColor: theme.palette.neutralQuaternary,
      width: 30,
      minWidth: 56,
      padding: '12px 5px',
    },
    clearFilterButton: {
      fontSize: 12,
    },
    filterButtonStackItem: {
      paddingTop: 29,
    },
    dropdownTitle: {
      borderRadius: 4,
      borderStyle: 'solid',
      borderWidth: 1,
      borderColor: theme.palette.neutralQuaternary,
      backgroud: theme.palette.white,
      width: 150
    },
    textFieldFieldGroup: {
      borderRadius: 4,
      border: '1px solid',
      borderColor: theme.palette.neutralQuaternary,
      backgroud: theme.palette.white,
      width: 150
    },
    clearFilterTooltip: {
      display: 'inline-block',
    },
    clearFilterIconButton: {
      fontSize: 20,
    }
  };
};