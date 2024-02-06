// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import type { HRQuerySourceStyleProps, HRQuerySourceStyles } from './HRQuerySource.types';

export const getStyles = (props: HRQuerySourceStyleProps): HRQuerySourceStyles => {
  const { theme } = props;

  return {
    textFieldFieldGroup: {
      borderRadius: 4,
      border: '1px solid',
      borderColor: theme.palette.neutralQuaternary,
      background: theme.palette.white,
      width: 300
    },
    labelContainer: {
      display: 'flex',
      alignItems: 'center'
    },
    horizontalChoiceGroup: {
      display: 'flex',
      flexDirection: 'row',
    },
    horizontalChoiceGroupContainer: {
      display: 'flex',
      flexDirection: 'row',
      '> *': { marginRight: '20px' }
    },
    error:{
      color: theme.semanticColors.errorText,
      fontSize: 12,
      fontFamily: 'Segoe UI',
      fontWeight: 400,
      marginTop: 4,
      marginBottom: 4
  }
  };
};