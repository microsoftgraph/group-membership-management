// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type IStyle, type IStyleFunctionOrObject, type ITheme, type IPersonaProps } from '@fluentui/react';
import type React from 'react';

export type OrgLeaderStyles = {
  root: IStyle;
  labelContainer: IStyle;
  textField: IStyle;
  textFieldGroup: IStyle;
  suggestionItems: IStyle;
  error: IStyle;
};

export type OrgLeaderStyleProps = {
  className?: string;
  theme: ITheme;
};

// Avoid conflicting onChange from HTML div attributes
export type OrgLeaderProps = Omit<React.AllHTMLAttributes<HTMLDivElement>, 'onChange'> & {
  /** Optional className to apply to the root of the component. */
  className?: string;

  /** Customized styling layered on top of variant rules. */
  styles?: IStyleFunctionOrObject<OrgLeaderStyleProps, OrgLeaderStyles>;

  /** Selected items to display in the picker. */
  selectedItems?: IPersonaProps[];

  /** Whether the picker should be disabled. */
  disabled?: boolean;

  /** Called to resolve suggestions when typing. */
  onResolveSuggestions: (
    text: string,
    currentItems?: IPersonaProps[]
  ) => Promise<IPersonaProps[]> | IPersonaProps[];

  /** Called when input text changes; should return the same text or a modified one. */
  onInputChange: (text: string) => string;

  /** Called when selected items change. */
  onChange: (items?: IPersonaProps[]) => void;

  /** Whether to show an error message below the picker. */
  showError?: boolean;

  /** Optional test id applied to the picker. Default: 'hr-org-leader-picker' */
  dataTestId?: string;

  /** Optional callout width for the suggestions. Default: 300 */
  calloutWidth?: number;

  /** Optional tooltip id for accessibility. If not provided, a unique id will be generated. */
  tooltipId?: string;
};