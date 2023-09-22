// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
  IDropdownStyleProps,
  IDropdownStyles,
  type IStyle,
  type IStyleFunctionOrObject,
  type ITheme,
} from '@fluentui/react';
import type React from 'react';

export interface IJobsListFilterStyles {
  root: IStyle;
  container: IStyle;
  filterButton: IStyle;
  clearFilterButton: IStyle;
  filterButtonStackItem: IStyle;
  dropdownTitle: IStyle;
  textFieldFieldGroup: IStyle;
  clearFilterTooltip: IStyle;
  clearFilterIconButton: IStyle;
}

export interface IJobsListFilterStyleProps {
  className?: string;
  theme: ITheme;
}

export interface IJobsListFilterProps
  extends React.AllHTMLAttributes<HTMLDivElement> {
  /**
   * Optional className to apply to the root of the component.
   */
  className?: string;

  /**
   * Call to provide customized styling that will layer on top of the variant rules.
   */
  styles?: IStyleFunctionOrObject<IJobsListFilterStyleProps, IJobsListFilterStyles>;
  filterStatus: string | undefined;
  filterActionRequired: string | undefined;
  filterID: string | undefined;
  getJobsByPage: () => void;
  setFilterStatus: (status: string) => void;
  setFilterActionRequired: (actionRequired: string) => void;
  setFilterID: (ID: string) => void;
}
