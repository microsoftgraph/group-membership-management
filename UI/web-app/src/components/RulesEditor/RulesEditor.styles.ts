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
    addButton: {
      flex: '0 0 auto',
    },
    details: {
      marginTop: 16,
      paddingTop: 18,
      paddingBottom: 18,
      paddingLeft: 22,
      paddingRight: 22,
      borderRadius: 10,
      backgroundColor: theme.palette.white,
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
