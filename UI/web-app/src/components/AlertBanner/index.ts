// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import { AlertBannerBase } from './AlertBanner.base';
import { getStyles } from './AlertBanner.styles';
import { IAlertBannerProps, IAlertBannerStyleProps, IAlertBannerStyles } from './AlertBanner.types';

export const AlertBanner = styled<IAlertBannerProps, IAlertBannerStyleProps, IAlertBannerStyles>(
  AlertBannerBase,
  getStyles
);

export * from './AlertBanner.types';
export { isAlertBannerVisible } from './AlertBanner.base';
