import React from 'react';
import { IStyle, IStyleFunctionOrObject, ITheme } from '@fluentui/react';

export interface IPageHeaderStyles {
  root: IStyle;
}

export interface IPageHeaderStyleProps {
  className?: string;
  theme: ITheme;
}

export interface IPageHeaderProps extends React.AllHTMLAttributes<HTMLDivElement> {
  /**
   * Optional className to apply to the root of the component.
   */
  className?: string;

  /**
   * Call to provide customized styling that will layer on top of the variant rules.
   */
  styles?: IStyleFunctionOrObject<IPageHeaderStyleProps, IPageHeaderStyles>;

}
