// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type IStyle, type IStyleFunctionOrObject } from '@fluentui/react';

export interface IAlertBannerStyles {
  root: IStyle;
  messageBar: IStyle;
  link: IStyle;
}

export interface IAlertBannerStyleProps {
  className?: string;
}

/**
 * Props for the AlertBanner component. The banner sources its configuration
 * from the Redux store, so no data props are required.
 */
export interface IAlertBannerProps {
  className?: string;
  styles?: IStyleFunctionOrObject<IAlertBannerStyleProps, IAlertBannerStyles>;
}
