// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { NeutralColors } from '@fluentui/react';
import type { MembershipLookupStyleProps, MembershipLookupStyles } from './MembershipLookup.types';

export const getStyles = (props: MembershipLookupStyleProps): MembershipLookupStyles => {
  const { theme } = props;
  // The dark theme inverts only the neutral ramp (see theme/palette.ts), so themeLighterAlt stays
  // near-white and would render the result card's neutralPrimary text light-on-light in dark mode.
  // Fall back to a neutral-ramp surface in dark mode only; leave light mode on the accent ramp.
  const isDarkMode = theme.palette.white.toLowerCase() === NeutralColors.gray220;

  return {
    root: {
      marginBottom: 0,
    },
    title: {
      fontWeight: 600,
      fontSize: 16,
      marginBottom: 4,
    },
    description: {
      fontSize: 14,
      color: theme.palette.neutralSecondary,
      marginBottom: 12,
    },
    pickerWrapper: {
      position: 'relative',
      width: '100%',
      maxWidth: 260,
      selectors: {
        '& .ms-BasePicker': {
          width: '100%',
        },
        '& .ms-BasePicker-text': {
          paddingRight: 32,
        },
      },
    },
    picker: {
      borderRadius: 4,
      border: '1px solid',
      borderColor: theme.palette.neutralQuaternary,
      backgroundColor: theme.palette.white,
      width: '100%',
      boxSizing: 'border-box',
    },
    pickerTrailingIcon: {
      position: 'absolute',
      right: 8,
      top: '50%',
      transform: 'translateY(-50%)',
      fontSize: 16,
      color: theme.palette.neutralSecondary,
      pointerEvents: 'none',
    },
    resultCard: {
      marginTop: 12,
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'space-between',
      gap: 16,
      padding: '12px 16px',
      borderRadius: 10,
      backgroundColor: isDarkMode ? theme.palette.neutralLighterAlt : theme.palette.themeLighterAlt,
      ...(isDarkMode
        ? { border: `1px solid ${theme.palette.neutralLight}`, color: theme.palette.neutralPrimary }
        : {}),
    },
    resultPersona: {
      flexShrink: 0,
    },
    resultStatuses: {
      display: 'grid',
      gridTemplateColumns: 'auto auto',
      columnGap: '6px',
      rowGap: '6px',
      alignItems: 'center',
      fontSize: 14,
      flexShrink: 0,
    },
    statusLabel: {
      fontWeight: 600,
      color: theme.palette.neutralPrimary,
      textAlign: 'right',
      justifySelf: 'end',
    },
    statusValue: {
      display: 'inline-flex',
      alignItems: 'center',
      gap: 4,
      color: theme.palette.neutralPrimary,
      justifySelf: 'start',
    },
    statusIcon: {
      fontSize: 16,
    },
    dashedCircle: {
      display: 'inline-block',
      width: 14,
      height: 14,
      borderRadius: '50%',
      border: `1.5px dashed ${theme.palette.neutralTertiary}`,
      boxSizing: 'border-box',
      flexShrink: 0,
    },
    errorMessage: {
      marginTop: 12,
    },
    spinnerContainer: {
      display: 'flex',
      alignItems: 'center',
      gap: 8,
      marginTop: 12,
    },
  };
};
