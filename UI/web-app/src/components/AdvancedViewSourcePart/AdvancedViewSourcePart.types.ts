// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    type IStyle,
    type IStyleFunctionOrObject,
    type ITheme,
  } from '@fluentui/react';
  import type React from 'react';
  import type { SourcePartQuery } from '../../models/ISourcePart';
  
  export interface IAdvancedViewSourcePartStyles {
    root: IStyle;
    textField: IStyle;
    textFieldGroup: IStyle;
    button: IStyle;
    successMessage: IStyle;
    errorMessage: IStyle;
  }
  
  export interface IAdvancedViewSourcePartStyleProps {
    className?: string;
    theme: ITheme;
  }
  
  export interface IAdvancedViewSourcePartProps
    extends React.AllHTMLAttributes<HTMLDivElement> {
    /**
     * Optional className to apply to the root of the component.
     */
    className?: string;
  
    /**
     * Call to provide customized styling that will layer on top of the variant rules.
     */
    styles?: IStyleFunctionOrObject<IAdvancedViewSourcePartStyleProps, IAdvancedViewSourcePartStyles>;
    query: SourcePartQuery;
    partId: number;
    onValidate: (isValid: boolean, partId: number) => void;
  }
  