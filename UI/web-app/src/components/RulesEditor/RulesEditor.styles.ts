// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { RulesEditorStyleProps, RulesEditorStyles } from './RulesEditor.types';

export const getStyles = (props: RulesEditorStyleProps): RulesEditorStyles => {
  const { className, theme } = props;
  return {
    root: [className, {}],
    onboardingChoices: {
      display: 'flex',
      flexDirection: 'column',
      gap: 14,
      color: theme.palette.neutralPrimary,
    },
    recommendation: {
      display: 'flex',
      alignItems: 'flex-start',
      gap: 2,
      padding: '2px 8px',
      fontSize: 14,
      lineHeight: '20px',
      selectors: { '> img': { flexShrink: 0 } },
    },
    recommendationTitle: {
      margin: 0,
      fontSize: 16,
      lineHeight: '22px',
      fontWeight: 600,
    },
    choiceCards: {
      display: 'grid',
      gridTemplateColumns: 'repeat(2, minmax(0, 1fr))',
      gap: 8,
      selectors: {
        '@media (max-width: 640px)': { gridTemplateColumns: 'minmax(0, 1fr)' },
      },
    },
    choiceCard: {
      display: 'flex',
      flexDirection: 'column',
      alignItems: 'center',
      gap: 8,
      padding: '12px 8px',
      border: `1px solid ${theme.palette.neutralTertiaryAlt}`,
      borderRadius: 8,
      backgroundColor: theme.palette.white,
      minWidth: 0,
      selectors: { '> button': { marginTop: 'auto', maxWidth: '100%', whiteSpace: 'normal', height: 'auto', minHeight: 32 } },
    },
    choiceCopy: { width: '100%', fontSize: 12, lineHeight: '16px' },
    choiceTitle: { margin: 0, fontSize: 12, lineHeight: '16px', fontWeight: 600 },
    switchHint: {
      display: 'flex',
      alignItems: 'center',
      gap: 4,
      fontSize: 12,
      lineHeight: '16px',
      selectors: { '> i': { fontSize: 16, color: theme.palette.neutralSecondary } },
    },
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
