// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    type IAdvancedViewSourcePartStyleProps,
    type IAdvancedViewSourcePartStyles,
  } from './AdvancedViewSourcePart.types';
  
  export const getStyles = (props: IAdvancedViewSourcePartStyleProps): IAdvancedViewSourcePartStyles => {
    const { className, theme } = props;
  
    return {
      root: [{
        fontWeight: 400,
        fontSize: 16,
        lineHeight: 22,
        fontFamily: 'Segoe UI',
        gap: 16,
        display: 'flex',
        flexDirection: 'column',
        width: '100%',
      }, className],
      textField: {
        fontWeight: 400,
        fontSize: 16,
        fontFamily: 'Segoe UI',
        borderRadius: 4,
        borderStyle: 'solid',
        borderWidth: 1,
        borderColor: theme.palette.neutralQuaternary,
      },
      textFieldGroup: {
        border: 'none',
      },
      button: {
        width: 'fit-content',
      },
      errorMessage: {
        fontWeight: 400,
        fontSize: 12,
        lineHeight: 16,
        fontFamily: 'Segoe UI',
        color: theme.semanticColors.errorText,
      },
      successMessage: {
        fontWeight: 400,
        fontSize: 12,
        lineHeight: 16,
        fontFamily: 'Segoe UI',
        color: theme.semanticColors.successIcon,
      },
    };
  };
  