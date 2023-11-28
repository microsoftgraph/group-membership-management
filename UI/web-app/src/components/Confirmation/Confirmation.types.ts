// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    type IStyle,
    type IStyleFunctionOrObject,
    type ITheme,
  } from '@fluentui/react';
  import type React from 'react';
  
  export interface IConfirmationStyles {
    root: IStyle;
    ConfirmationContainer: IStyle;
    cardHeader: IStyle;
    cardTitle: IStyle;
    itemTitle: IStyle;
    itemData: IStyle;
    endpointsContainer: IStyle;
  }
  
  export interface IConfirmationStyleProps {
    className?: string;
    theme: ITheme;
  }
  
  export interface IConfirmationProps
    extends React.AllHTMLAttributes<HTMLDivElement> {
  
    /**
     * Optional className to apply to the root of the component.
     */
    className?: string;
  
    /**
     * Call to provide customized styling that will layer on top of the variant rules.
     */
    styles?: IStyleFunctionOrObject<IConfirmationStyleProps, IConfirmationStyles>;
    onEditButtonClick: (stepToEdit: number) => void;
  }
  