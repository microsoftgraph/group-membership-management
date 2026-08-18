// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import type { HyperlinkSettingStyleProps, HyperlinkSettingStyles } from './HyperlinkSetting.types';

export const getStyles = (props: HyperlinkSettingStyleProps): HyperlinkSettingStyles => {
  const { className, theme } = props;

  return {
    root: [
      {
        padding: '0px 14px',
        display: 'flex',
        alignItems: 'flex-start',
        gap: '15px',
      },
      className,
    ],
    card: {
      borderRadius: 10,
      backgroundColor: theme.palette.white,
      outline: `1px solid ${theme.palette.neutralQuaternary}`,
      width: '100%',
      minWidth: 0,
      boxSizing: 'border-box',
      padding: 24,
      display: 'flex',
      flexDirection: 'column',
      alignItems: 'stretch',
      gap: '4px',
    },
    title: {
      fontWeight: 600,
      fontSize: 16,
    },
    description: {
      fontSize: 14,
      fontWeight: 400,
    },
    fieldContainer: {
      width: '100%',
    },
    textFieldFieldGroup: {
      borderRadius: 4,
      border: '1px solid',
      borderColor: theme.palette.neutralQuaternary,
      backgroud: theme.palette.white,
      width: '100%',
    },
  };
};
