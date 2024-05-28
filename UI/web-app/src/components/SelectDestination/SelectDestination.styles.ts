// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
  type ISelectDestinationStyleProps,
  type ISelectDestinationStyles,
} from './SelectDestination.types';

export const getStyles = (props: ISelectDestinationStyleProps): ISelectDestinationStyles => {
  const { className, theme } = props;

  return {
    root: [{
      paddingTop: 18,
      paddingBottom: 18,
      paddingLeft: 22,
      paddingRight: 22,
      borderRadius: 10,
      marginBottom: 12,
      backgroundColor: theme.palette.white
    }, className],
    dropdownTitle: {
      borderRadius: 4,
      borderStyle: 'solid',
      borderWidth: 1,
      borderColor: theme.palette.neutralQuaternary,
      background: theme.palette.white,
      width: 500
    },
    selectDestinationContainer: {
      display: 'flex',
      flexDirection: 'column',
      gap: 16
    },
    dropdownField: {
      width: 500
    },
    peoplePicker: {
      width: 500,
      borderRadius: 4,
      borderStyle: 'solid',
      borderWidth: 1,
      borderColor: theme.palette.neutralQuaternary,
      selectors: {
        '&::after': {
          borderColor: theme.palette.neutralQuaternary,
          content: 'none'
        }
      }
    },
    ownershipWarning: {
      fontWeight: 400,
      fontSize: 12,
      lineHeight: 16,
      fontFamily: 'Segoe UI',
      color: theme.semanticColors.errorText,
    },
    resultsContainer: {
      display: 'flex',
      flexDirection: 'column',
      gap: 16,
    },
    endpointsContainer: {
      display: 'flex',
      flexDirection: 'column',
      gap: 23,
      overflow: 'auto'
    },
    outlookWarning: {
      width: 'fit-content',
      display: 'flex',
      alignItems: 'center'
    },
    outlookContainer: {
      display: 'flex',
      flexDirection: 'row',
      gap: 70
    },
    spinnerContainer: {
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      minHeight: 20
    }
  };
};
