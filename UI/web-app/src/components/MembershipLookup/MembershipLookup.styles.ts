// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import type { MembershipLookupStyleProps, MembershipLookupStyles } from './MembershipLookup.types';

export const getStyles = (props: MembershipLookupStyleProps): MembershipLookupStyles => {
  const { theme } = props;

  return {
    root: {
      marginBottom: 16,
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
    picker: {
      borderRadius: 4,
      border: '1px solid',
      borderColor: theme.palette.neutralQuaternary,
      backgroundColor: theme.palette.white,
      maxWidth: 360,
    },
    resultCard: {
      marginTop: 12,
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'space-between',
      gap: 16,
      padding: '12px 16px',
      borderRadius: 10,
      backgroundColor: theme.palette.themeLighterAlt,
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
