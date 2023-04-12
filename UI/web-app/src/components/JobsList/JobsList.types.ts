import React from 'react';
import { IStyle, IStyleFunctionOrObject, ITheme } from '@fluentui/react';

export interface IJobsListStyles {
  root: IStyle;
}

export interface IJobsListStyleProps {
  className?: string;
  theme: ITheme;
}

export interface IJobsListProps extends React.AllHTMLAttributes<HTMLDivElement> {
  /**
   * Optional className to apply to the root of the component.
   */
  className?: string;

  /**
   * Call to provide customized styling that will layer on top of the variant rules.
   */
  styles?: IStyleFunctionOrObject<IJobsListStyleProps, IJobsListStyles>;
}