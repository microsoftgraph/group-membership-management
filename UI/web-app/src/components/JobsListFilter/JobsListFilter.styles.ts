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
      padding: '16px 24px 24px 33px',
      height: '300'
    },
    filterHeaderContainer: {
      paddingVertical: 4,
      marginBottom: 16,
    },
    filterButton: {
      fontSize: 14,
      borderRadius: 4,
      border: '1px solid',
      borderColor: theme.palette.neutralQuaternary,
      padding: '12px 5px',
      marginRight: 15,
      textAlign: 'center',
      fontFamily: 'Segoe UI',
      fontWeight: 400,
    },
    filterTitleText: {
      fontSize: 20,
      fontWeight: 600,
      fontFamily: 'Segoe UI',
      fontColor: theme.palette.black,
    },
    clearFilterButton: {
      fontSize: 14,
      borderRadius: 4,
      border: '1px solid',
      borderColor: theme.palette.neutralQuaternary,
      padding: '12px 5px',
      textAlign: 'center',
      fontFamily: 'Segoe UI',
      fontWeight: 400,
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
    textFieldFieldGroupGuid: {
      borderRadius: 4,
      border: '1px solid',
      borderColor: theme.palette.neutralQuaternary,
      backgroud: theme.palette.white,
      width: 320
    },
    clearFilterTooltip: {
      display: 'inline-block',
    },
    filterButtonIcon: {
      fontSize: 14,
    },
    filterInputsContainer: {
      width: '100%',
      overflowX: 'auto',
      overflowY: 'hidden',
    },
    filterInputsStack: {
      display: 'flex',
      flexWrap: 'nowrap',
    },
    peoplePicker: {
      borderRadius: 4,
      border: '1px solid',
      borderColor: theme.palette.neutralQuaternary,
      backgroud: theme.palette.white,
      width: 150
    },
    emptyStackItem: {
      width: 150,
    }
  };
};