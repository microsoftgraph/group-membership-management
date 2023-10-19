// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
  IComboBox,
  IComboBoxOption,
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
  searchField: IStyle;
  comboBoxContainer: IStyle;
  comboBoxInput: IStyle;
  endpointsContainer: IStyle;
  outlookWarning: IStyle;
  outlookContainer: IStyle;
  ownershipWarning: IStyle;
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
  onSearchDestinationChange?: (event: React.FormEvent<IComboBox>, option?: IComboBoxOption) => void;
  selectedDestination?: Destination;
}
