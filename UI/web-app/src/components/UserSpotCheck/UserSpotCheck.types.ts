// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type IStyle, type IStyleFunctionOrObject, type ITheme } from '@fluentui/react';
import type React from 'react';
import { ISourcePart } from '../../models/ISourcePart';

export type UserSpotCheckStyles = {
  root: IStyle;
  title: IStyle;
  description: IStyle;
  picker: IStyle;
  resultsContainer: IStyle;
  partRow: IStyle;
  partTitle: IStyle;
  statusBadge: IStyle;
  statusIcon: IStyle;
  errorMessage: IStyle;
  banner: IStyle;
  spinnerContainer: IStyle;
};

export type UserSpotCheckStyleProps = {
  className?: string;
  theme: ITheme;
};

export type UserSpotCheckProps = React.AllHTMLAttributes<HTMLDivElement> & {
  /**
   * Optional className to apply to the root of the component.
   */
  className?: string;

  /**
   * Call to provide customized styling that will layer on top of the variant rules.
   */
  styles?: IStyleFunctionOrObject<UserSpotCheckStyleProps, UserSpotCheckStyles>;

  /**
   * The sync job id to spot-check the user against.
   */
  syncJobId: string;

  /**
   * The source parts of the job, used to display per-part titles.
   */
  sourceParts: ISourcePart[];
};
