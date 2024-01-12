// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    type IStyle,
    type IStyleFunctionOrObject,
    type ITheme,
  } from '@fluentui/react';
  import type React from 'react';
  
  export interface IOnboardingStepStyles {
    root: IStyle;
    titleCard: IStyle;
    title: IStyle;
    stepTitle: IStyle;
    stepDescription: IStyle;
    destination: IStyle;
  }
  
  export interface IOnboardingStepStyleProps {
    className?: string;
    theme: ITheme;
  }
  
  export interface IOnboardingStepProps
    extends React.AllHTMLAttributes<HTMLDivElement> {

    /**
     * Optional className to apply to the root of the component.
     */
    className?: string;
  
    /**
     * Call to provide customized styling that will layer on top of the variant rules.
     */
    styles?: IStyleFunctionOrObject<IOnboardingStepStyleProps, IOnboardingStepStyles>;
    stepTitle: string;
    stepDescription: string;
    destinationType?: string;
    destinationName?: string;
  }
  