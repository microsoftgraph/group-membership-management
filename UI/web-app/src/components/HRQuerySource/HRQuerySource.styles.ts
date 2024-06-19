// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import type { HRQuerySourceStyleProps, HRQuerySourceStyles } from './HRQuerySource.types';

export const getStyles = (props: HRQuerySourceStyleProps): HRQuerySourceStyles => {
  const { className, theme } = props;

  return {
    root: [{
      fontWeight: 400,
      fontSize: 16,
      lineHeight: 22,
      fontFamily: 'Segoe UI',
      display: 'flex',
      flexDirection: 'column',
      width: '100%',
      maxWidth: 600
    }, className],
    horizontalChoiceGroup: {
      display: 'flex',
      flexDirection: 'row'
    },
    horizontalChoiceGroupContainer: {
      display: 'flex',
      flexDirection: 'row',
      '> *': { marginRight: '20px' },
      flexWrap: 'wrap'
    },
    expandButton: {
      fontSize: 12,
      height: 14
    },
    separator: {
      minWidth: 1200
    },
    cardHeader: {
      display: 'flex',
      flexDirection: 'row',
      alignItems: 'space-between',
      marginTop: 50,
      minWidth: 1200
    },
    cardTitle: {
      flex: 1,
      fontSize: 16,
      fontStyle: 'normal',
      fontWeight: 'normal',
      lineHeight: '22px'
    },
    labelContainer: {
      display: 'flex',
      alignItems: 'center'
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
    textFieldGroup: {
      border: 'none'
    },
    detailsList: {
      minWidth: 1200
    },
    betweenGroupsDropdown: {
      width: 100,
      marginLeft: 0
    },
    betweenChildrenDropdown: {
      maxWidth: 100,
      marginLeft: 70
    },
    startOfNestedGroupDropdown: {
      maxWidth: 100,
      marginLeft: 70
    },
    endOfNestedGroupDropdown: {
      maxWidth: 100,
      marginLeft: -50
    },
    detailsListWithBorder: {
      minWidth: 1200,
      borderStyle: 'solid',
      borderRadius: 15,
      borderColor: theme.palette.themeLighter,
      marginLeft: 50
    },
    dropdownTitle: {
      borderRadius: 4,
      borderStyle: 'solid',
      borderWidth: 1,
      borderColor: theme.palette.neutralQuaternary,
      minWidth: 200
    },
    upDown: {
      display: 'flex',
      flexDirection: 'column'
    },
    addAttribute: {
      marginLeft: 60
    },
    spinButton: {
      '&:after': {
        borderColor: theme.palette.neutralQuaternary
      },
      selectors: {
        [`@media (max-width: 600px)`]: {
          width: 10
        }
      }
    },
    removeButton: {
      color: theme.semanticColors.primaryButtonBackground,
      borderColor: theme.semanticColors.primaryButtonBackground,
      borderRadius: 4,
      border: 'none'
    },
    error: {
      fontWeight: 400,
      fontSize: 12,
      lineHeight: 16,
      fontFamily: 'Segoe UI',
      color: theme.semanticColors.errorText
    }
  };
};