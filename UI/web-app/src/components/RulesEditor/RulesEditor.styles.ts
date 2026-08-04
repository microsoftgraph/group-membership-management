// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { RulesEditorStyleProps, RulesEditorStyles } from './RulesEditor.types';

export const getStyles = (props: RulesEditorStyleProps): RulesEditorStyles => {
  const { className, theme } = props;
  return {
    root: [className, {}],
    headerBar: {
      display: 'flex',
      flexDirection: 'row',
      alignItems: 'center',
      justifyContent: 'space-between',
      gap: 12,
      marginBottom: 12,
    },
    description: {
      color: theme.palette.neutralSecondary,
      fontSize: 13,
      flex: '1 1 auto',
    },
    headerActions: {
      display: 'flex',
      flexDirection: 'row',
      alignItems: 'center',
      gap: 12,
      flex: '0 0 auto',
    },
    addButton: {
      flex: '0 0 auto',
      height: 32,
      borderRadius: 4,
      padding: '5px 12px',
      selectors: {
        '.ms-Button-flexContainer': {
          gap: 4,
        },
      },
    },
    details: {
      marginTop: 16,
    },
    emptyState: {
      display: 'flex',
      flexDirection: 'column',
      alignItems: 'center',
      justifyContent: 'center',
      gap: 8,
      textAlign: 'center',
      padding: '40px 24px',
      borderRadius: 10,
      border: `1px dashed ${theme.palette.neutralTertiaryAlt}`,
      backgroundColor: theme.palette.neutralLighterAlt,
    },
    emptyTitle: {
      fontSize: 16,
      fontWeight: 600,
      color: theme.palette.neutralPrimary,
    },
    emptyDescription: {
      fontSize: 13,
      color: theme.palette.neutralSecondary,
      maxWidth: 360,
      marginBottom: 8,
    },
  };
};
