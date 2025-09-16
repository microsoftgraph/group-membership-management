// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import type { HRQueryItemColumnStyleProps, HRQueryItemColumnStyles } from './HRQueryItemColumn.types';

export const getStyles = (props: HRQueryItemColumnStyleProps): HRQueryItemColumnStyles => {
  const { className, theme } = props;

  return {
    root: [{
      display: 'flex',
      alignItems: 'center',
      width: '100%',
    }, className],
    upDown: {
      display: 'flex',
      flexDirection: 'column',
      alignItems: 'center',
      justifyContent: 'center',
      width: 20,
      minWidth: 20,
      maxWidth: 20,
    },
    attribute: {
      width: 200,
      minWidth: 200,
      maxWidth: 200,
    },
    equalityOperator: {
      width: 200,
      minWidth: 200,
      maxWidth: 200,
    },
    value: {
      width: 200,
      minWidth: 200,
      maxWidth: 200,
    },
    andOr: {
      width: 200,
      minWidth: 200,
      maxWidth: 200,
    },
    remove: {
      width: 200,
      minWidth: 200,
      maxWidth: 200,
    },
    removeButton: {
      color: theme.semanticColors.primaryButtonBackground,
      borderColor: theme.semanticColors.primaryButtonBackground,
      borderRadius: 4,
      border: 'none'
    },
    removeButtonDisabled: {
      opacity: 0.5,
      cursor: 'not-allowed'
    },
    textField: {
      fontWeight: 300,
      fontSize: 16,
      fontFamily: 'Segoe UI',
      borderRadius: 4,
      borderStyle: 'solid',
      borderWidth: 1,
      borderColor: theme.palette.neutralQuaternary,
      minWidth: 200
    },
    dropdownTitle: {
      borderRadius: 4,
      borderStyle: 'solid',
      borderWidth: 1,
      borderColor: theme.palette.neutralQuaternary,
      minWidth: 200
    },
    errorMessageStyles: {
      fontWeight: 400,
      fontSize: 12,
      lineHeight: 16,
      fontFamily: 'Segoe UI',
      color: theme.semanticColors.errorText,
      whiteSpace: 'normal',
      wordWrap: 'break-word',
      overflowWrap: 'break-word',
    },
    comboBoxOptionCodeText: { 
      fontStyle: 'italic',
    },
    comboBoxOptionContainer: { 
      paddingTop: 3,
      paddingBottom: 3,
    },
    comboBoxOptionList: { 
      maxHeight: 300,
    },
    readOnlyComboBox: {
      backgroundColor: theme.palette.neutralLighter,
      cursor: 'pointer'
    },
    readOnlyComboBoxInput: {
      backgroundColor: theme.palette.neutralLighter,
      color: theme.palette.neutralDark,
      cursor: 'pointer'
    },
  };
};
