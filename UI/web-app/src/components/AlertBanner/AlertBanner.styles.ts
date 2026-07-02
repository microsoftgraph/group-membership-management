// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type IStyleFunctionOrObject } from '@fluentui/react';
import { IAlertBannerStyleProps, IAlertBannerStyles } from './AlertBanner.types';

export const getStyles: IStyleFunctionOrObject<IAlertBannerStyleProps, IAlertBannerStyles> = () => {
  return {
    root: {
      width: '100%',
      zIndex: 100,
    },
    messageBar: {
      width: '100%',
      // Ensure the message content spans the full app width rather than the
      // default MessageBar max-width.
      selectors: {
        '.ms-MessageBar-content': {
          maxWidth: '100%',
        },
      },
    },
    link: {
      marginLeft: 4,
    },
  };
};
