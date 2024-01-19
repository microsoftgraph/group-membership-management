// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    type IStyle,
    type IStyleFunctionOrObject,
    type ITheme,
  } from '@fluentui/react';
  import type React from 'react';
import { HRSourcePart } from '../../models/HRSourcePart';
import { SyncJobQuery } from '../../models/SyncJobQuery';
  
  export interface IAdvancedQueryStyles {
    root: IStyle;
    textField: IStyle;
    textFieldGroup: IStyle;
    button: IStyle;
    successMessage: IStyle;
    errorMessage: IStyle;
  }
  
  export interface IAdvancedQueryStyleProps {
    className?: string;
    theme: ITheme;
  }
  
  export interface IAdvancedQueryProps
    extends React.AllHTMLAttributes<HTMLDivElement> {
    /**
     * Optional className to apply to the root of the component.
     */
    className?: string;
  
    /**
     * Call to provide customized styling that will layer on top of the variant rules.
     */
    styles?: IStyleFunctionOrObject<IAdvancedQueryStyleProps, IAdvancedQueryStyles>;
    query: SyncJobQuery | (HRSourcePart);
    onQueryChange: (event: React.FormEvent<HTMLTextAreaElement | HTMLInputElement>, newValue?: SyncJobQuery | (HRSourcePart)) => void;
    partId: number;
    onValidate: (isValid: boolean, partId: number) => void;
  }
  