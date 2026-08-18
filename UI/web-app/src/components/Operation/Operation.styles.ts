// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import type { OperationStyleProps, OperationStyles } from './Operation.types';

export const getStyles = (props: OperationStyleProps): OperationStyles => {
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
      minWidth: 0,
      maxWidth: '100%',
      boxSizing: 'border-box',
      padding: 24,
      display: 'flex',
      flexDirection: 'column',
      justifyContent: 'space-between',
      gap: '12px',
    },
    titleRow: {
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'space-between',
      gap: '12px',
      width: '100%',
    },
    title: {
      fontWeight: 600,
      fontSize: 16,
    },
    buttonContainer: {
      display: 'flex',
      alignItems: 'center',
      gap: '8px',
      flexShrink: 0,
    },
    button: {
      borderRadius: 4,
      minWidth: 80,
    },
    description: {
      fontSize: 14,
      fontWeight: 400,
    },
  };
};
