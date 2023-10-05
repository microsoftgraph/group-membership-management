// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
  type IStyle,
  type IStyleFunctionOrObject,
  type ITheme,
} from '@fluentui/react';
import type React from 'react';

export interface IJobsListStyles {
  root: IStyle;
  enabled: IStyle;
  disabled: IStyle;
  titleContainer: IStyle;
  title: IStyle;
  actionRequired: IStyle;
  tabContent: IStyle;
  columnToEnd: IStyle;
  refresh: IStyle;
  jobsList: IStyle;
  jobsListFilter: IStyle;
  footer: IStyle;
  noMembershipsFoundText: IStyle;
  errorMessageBar: IStyle;
}

export interface IJobsListStyleProps {
  className?: string;
  theme: ITheme;
}

export interface IJobsListProps
  extends React.AllHTMLAttributes<HTMLDivElement> {
  /**
   * Optional className to apply to the root of the component.
   */
  className?: string;

  /**
   * Call to provide customized styling that will layer on top of the variant rules.
   */
  styles?: IStyleFunctionOrObject<IJobsListStyleProps, IJobsListStyles>;
}
