// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type IStyle, type IStyleFunctionOrObject, type ITheme } from '@fluentui/react';
import type React from 'react';

export type MembershipLookupStyles = {
  root: IStyle;
  title: IStyle;
  description: IStyle;
  pickerWrapper: IStyle;
  picker: IStyle;
  pickerTrailingIcon: IStyle;
  resultCard: IStyle;
  resultPersona: IStyle;
  resultStatuses: IStyle;
  statusLabel: IStyle;
  statusValue: IStyle;
  statusIcon: IStyle;
  dashedCircle: IStyle;
  errorMessage: IStyle;
  spinnerContainer: IStyle;
};

export type MembershipLookupStyleProps = {
  className?: string;
  theme: ITheme;
};

export type MembershipLookupProps = React.AllHTMLAttributes<HTMLDivElement> & {
  /**
   * Optional className to apply to the root of the component.
   */
  className?: string;

  /**
   * Call to provide customized styling that will layer on top of the variant rules.
   */
  styles?: IStyleFunctionOrObject<MembershipLookupStyleProps, MembershipLookupStyles>;

  /**
   * The sync job id to look the user up against.
   */
  syncJobId: string;

  /**
   * The run id of the threshold-exceeded run, used to determine the pending change
   * ("After changes") for the selected user.
   */
  runId: string;
};
