// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { IDropdownStyles, ITextFieldStyles, ITooltipHostStyles } from '@fluentui/react';
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
      border: '1px solid #D1D1D1',
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
  };
};

export const dropdownStyles : Partial<IDropdownStyles> = {
  dropdown: { width: 150 },
  title: { borderRadius: 4, border: '1px solid #D2D0CE', backgroud: '#FFF', width: 150 },
};

export const textFieldStyles :  Partial<ITextFieldStyles> = {
  fieldGroup:{ borderRadius: 4, border: '1px solid #D2D0CE', backgroud: '#FFF', width: 150  },
}

export const clearFilterTooltipStyles : Partial<ITooltipHostStyles> = { 
  root: { display: 'inline-block' },
}
 