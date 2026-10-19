// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import type { IStyle } from '@fluentui/react';
import {
  type ISelectDestinationStyleProps,
  type ISelectDestinationStyles,
} from './SelectDestination.types';

export const getStyles = (props: ISelectDestinationStyleProps): ISelectDestinationStyles => {
  const { className, theme } = props;

  return {
    root: [{
    }, className],
    creationCards: {
      display: 'grid',
      gridTemplateColumns: 'repeat(2, minmax(0, 1fr))',
      gap: 18,
      borderTop: `1px solid ${theme.palette.neutralLight}`,
      paddingTop: 16,
      selectors: { '@media (max-width: 700px)': { gridTemplateColumns: '1fr' } },
    },
    creationCard: {
      border: `1px solid ${theme.palette.neutralQuaternary}`,
      borderRadius: 10,
      padding: 24,
      background: theme.semanticColors.bodyBackground,
    },
    creationCardHeader: {
      display: 'flex',
      alignItems: 'center',
      gap: 24,
      selectors: {
        '& h3': { margin: '0 0 8px', fontSize: 18, fontWeight: 600 },
        '& p': { margin: 0, lineHeight: 22 },
      },
    },
    creationCardIcon: {
      flexShrink: 0,
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      width: 54,
      height: 54,
      borderRadius: '50%',
      background: theme.palette.themePrimary,
      color: theme.palette.white,
      fontSize: 28,
    },
    creationCardAction: { display: 'flex', justifyContent: 'center', marginTop: 18 },
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
    channelSuggestionItem: {
      width: '100%',
      padding: '6px 12px',
      boxSizing: 'border-box',
      fontFamily: 'Segoe UI',
      fontSize: 14,
      lineHeight: '20px',
      whiteSpace: 'normal',
      overflowWrap: 'anywhere',
      wordBreak: 'break-word',
      textAlign: 'left',
    },
    ownershipWarning: {
      fontWeight: 400,
      fontSize: 12,
      lineHeight: 16,
      fontFamily: 'Segoe UI',
      color: theme.semanticColors.errorText,
      width: 500,
    },
    resultsContainer: {
      display: 'flex',
      flexDirection: 'column',
      gap: 16,
    },
    spinnerContainer: {
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      minHeight: 20
    },
    comboBoxOptionContainer: {
      paddingTop: 3,
      paddingBottom: 3,
      whiteSpace: 'normal',
      overflowWrap: 'anywhere',
      wordBreak: 'break-word',
    },
    comboBoxOptionCodeText: {
      fontStyle: 'italic',
      whiteSpace: 'normal',
      overflowWrap: 'anywhere',
      wordBreak: 'break-word',
    },
    textField: {
        fontWeight: 300,
        fontSize: 16,
        fontFamily: 'Segoe UI',
        borderRadius: 4,
        borderStyle: 'solid',
        borderWidth: 1,
        borderColor: theme.palette.neutralQuaternary,
        minWidth: 100,
        width: '20%'
    },
    textFieldGroup: {
        border: 'none'
    },
    messageBarContent: {
      color: 'inherit'
    },
    messageBarSection: {
      marginTop: 8
    },
    linkButton: {
      root: {
        border: 'none',
        backgroundColor: 'transparent',
        padding: '0px 0px',
      }
    },
    checkAgainButton: {
      root: {
        border: 'none',
        backgroundColor: 'transparent',
        padding: '0px 0px',
        color: 'inherit',
      },
      label: {
        color: 'inherit',
      }
    },
    suggestionItem: {
      selectors: {
        '& .ms-Suggestions-itemButton': {
          width: '100%',
          height: 'auto',
          minHeight: 40,
          maxHeight: 'none',
          padding: '4px 8px',
          textAlign: 'left',
        },
        '& .ms-Persona': {
          height: 'auto',
          alignItems: 'flex-start',
        },
        '& .ms-Persona-details': {
          height: 'auto',
          display: 'flex',
          flexDirection: 'column',
          justifyContent: 'center',
        },
        '& .ms-Persona-textContent': {
          height: 'auto',
          display: 'flex',
          flexDirection: 'column',
        },
        '& .ms-Persona-primaryText, & .ms-Persona-secondaryText, & .ms-Persona-tertiaryText, & .ms-Persona-optionalText': {
          position: 'static !important',
          whiteSpace: 'normal !important',
          overflow: 'visible !important',
          textOverflow: 'clip !important',
          overflowWrap: 'anywhere',
          wordBreak: 'break-word',
          height: 'auto',
        } as unknown as IStyle,
      },
    }
  };
};
