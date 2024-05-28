// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
  IPersonaProps,
  type IStyle,
  type IStyleFunctionOrObject,
  type ITheme,
} from '@fluentui/react';
import type React from 'react';
import { Destination } from '../../models/Destination';

export interface ISelectDestinationStyles {
  root: IStyle;
  selectDestinationContainer: IStyle;
  dropdownTitle: IStyle;
  dropdownField: IStyle;
  peoplePicker: IStyle;
  resultsContainer: IStyle;
  endpointsContainer: IStyle;
  outlookWarning: IStyle;
  outlookContainer: IStyle;
  ownershipWarning: IStyle;
  spinnerContainer: IStyle;
}

export interface ISelectDestinationStyleProps {
  className?: string;
  theme: ITheme;
}

export interface ISelectDestinationProps
  extends React.AllHTMLAttributes<HTMLDivElement> {

  /**
   * Optional className to apply to the root of the component.
   */
  className?: string;

  /**
   * Call to provide customized styling that will layer on top of the variant rules.
   */
  styles?: IStyleFunctionOrObject<ISelectDestinationStyleProps, ISelectDestinationStyles>;
  onSearchDestinationChange?: (selectedDestinations: IPersonaProps[] | undefined) => void;
  selectedDestination?: Destination;
}
