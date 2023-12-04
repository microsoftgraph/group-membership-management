// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
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
  dropdownTitle: IStyle;
  textFieldFieldGroup: IStyle;
  clearFilterTooltip: IStyle;
  filterButtonIcon: IStyle;
  filterHeaderContainer: IStyle;
  filterTitleText: IStyle;
  textFieldFieldGroupGuid: IStyle;
  filterInputsContainer: IStyle;
  filterInputsStack: IStyle;
  peoplePicker: IStyle;
  emptyStackItem: IStyle;
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
  getJobsByPage: () => void;
  setFilterStatus: (status: string) => void;
  setFilterActionRequired: (actionRequired: string) => void;
  setFilterDestinationId: (ID: string) => void;
  setFilterDestinationName: (ID: string) => void;
  setFilterDestinationOwner: (ID: string) => void;
  setFilterDestinationType: (ID: string) => void;
}
