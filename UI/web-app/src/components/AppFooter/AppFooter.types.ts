// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    type IStyle,
    type IStyleFunctionOrObject,
    type ITheme,
  } from '@fluentui/react';
  import type React from 'react';
  
  export interface IAppFooterStyles {
    root: IStyle;
    footer: IStyle;
  }
  
  export interface IAppFooterStyleProps {
    className?: string;
    theme: ITheme;
  }
  
  export interface IAppFooterProps
    extends React.AllHTMLAttributes<HTMLDivElement> {    
    /**
     * Optional className to apply to the root of the component.
     */
    className?: string;
  
    /**
     * Call to provide customized styling that will layer on top of the variant rules.
     */
    styles?: IStyleFunctionOrObject<IAppFooterStyleProps, IAppFooterStyles>;
    showPagingBar?: boolean;
  }
  