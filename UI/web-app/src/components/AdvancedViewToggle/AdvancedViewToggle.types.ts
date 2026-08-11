// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type IStyle, type IStyleFunctionOrObject, type ITheme } from '@fluentui/react';
import type React from 'react';

export type AdvancedViewToggleStyles = {
  root: IStyle;
};

export type AdvancedViewToggleStyleProps = {
  className?: string;
  theme: ITheme;
};

export type AdvancedViewToggleProps = React.AllHTMLAttributes<HTMLDivElement> & {
  /**
   * Optional className to apply to the root of the component.
   */
  className?: string;

  /**
   * Call to provide customized styling that will layer on top of the variant rules.
   */
  styles?: IStyleFunctionOrObject<AdvancedViewToggleStyleProps, AdvancedViewToggleStyles>;

  /**
   * Forces the toggle into read-only mode, where switching views only changes what is
   * displayed and never converts/rewrites source parts. Read-only is always enforced for
   * users without the Job Tenant Writer role, regardless of this value.
   */
  readOnly?: boolean;
};
