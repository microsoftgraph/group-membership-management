// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    type IStyle,
    type IStyleFunctionOrObject,
    type ITheme,
  } from '@fluentui/react';
  import type React from 'react';
  
  export interface IErrorStyles {
    root: IStyle;
    text: IStyle;
  }
  
  export interface IErrorStyleProps {
    className?: string;
    theme: ITheme;
  }
  
  export interface IErrorProps
    extends React.AllHTMLAttributes<HTMLDivElement> {

    /**
     * Optional className to apply to the root of the component.
     */
    className?: string;
  
    /**
     * Call to provide customized styling that will layer on top of the variant rules.
     */
    styles?: IStyleFunctionOrObject<IErrorStyleProps, IErrorStyles>;
  }
  