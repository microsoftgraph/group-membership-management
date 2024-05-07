// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    type IStyle,
    type IStyleFunctionOrObject,
    type ITheme,
  } from '@fluentui/react';
  import type React from 'react';
  
  export interface IRunConfigurationStyles {
    root: IStyle;
    horizontalChoiceGroup: IStyle;
    horizontalChoiceGroupContainer: IStyle;
    horizontalCheckboxes: IStyle;
    controlWidth: IStyle;
    checkboxDropdownPair: IStyle;
    checkboxPairsContainer: IStyle;
    thresholdDropdown: IStyle;
    dropdownTitle: IStyle;
    textFieldFieldGroup: IStyle;
  }
  
  export interface IRunConfigurationStyleProps {
    className?: string;
    theme: ITheme;
  }
  
  export interface IRunConfigurationProps
    extends React.AllHTMLAttributes<HTMLDivElement> {
  
    /**
     * Optional className to apply to the root of the component.
     */
    className?: string;
  
    /**
     * Call to provide customized styling that will layer on top of the variant rules.
     */
    styles?: IStyleFunctionOrObject<IRunConfigurationStyleProps, IRunConfigurationStyles>;
  }
  