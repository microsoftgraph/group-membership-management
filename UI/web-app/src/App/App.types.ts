import React from 'react';
import { IStyle, IStyleFunctionOrObject, ITheme } from '@fluentui/react';


export interface IAppStyles {
  root: IStyle;
  body: IStyle;
  nav: IStyle;
  content: IStyle;
}

export interface IAppStyleProps {
  className?: string;
  theme: ITheme;
}

export interface IAppProps extends React.AllHTMLAttributes<HTMLDivElement> {
  /**
   * Optional className to apply to the root of the component.
   */
  className?: string;

  /**
   * Call to provide customized styling that will layer on top of the variant rules.
   */
  styles?: IStyleFunctionOrObject<IAppStyleProps, IAppStyles>;

}