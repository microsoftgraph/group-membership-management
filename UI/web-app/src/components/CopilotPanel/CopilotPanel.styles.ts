// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    type ICopilotPanelStyleProps,
    type ICopilotPanelStyles,
} from './CopilotPanel.types';

export const getStyles = (props: ICopilotPanelStyleProps): ICopilotPanelStyles => {
    const { className, theme, experience } = props;
    const isUpdateExperience = experience === 'update';

    return {
        root: [{
            display: 'flex',
            flexDirection: 'column',
            height: '100%',
            minHeight: 0,
            backgroundColor: theme.palette.neutralLighterAlt,
        }, className],

        header: {
            minHeight: 67,
            boxSizing: 'border-box',
            flexShrink: 0,
            padding: '0 16px',
            borderBottom: `4px solid ${theme.palette.themePrimary}`,
            borderImage: isUpdateExperience ? undefined : `linear-gradient(90deg, ${theme.palette.themePrimary}, ${theme.palette.themeLight}, ${theme.palette.themeLighter}) 1`,
            backgroundColor: theme.palette.neutralLighterAlt,
            display: 'flex',
            flexDirection: 'column',
            justifyContent: 'center',
        },

        headerTitle: {
            fontSize: '20px',
            fontWeight: 600,
            lineHeight: '28px',
            color: theme.palette.neutralPrimary,
        },

        headerSubtitle: {
            fontSize: '10px',
            lineHeight: '14px',
            color: theme.palette.neutralPrimary,
        },

        chatContainer: {
            display: 'flex',
            flexDirection: 'column',
            flex: 1,
            overflow: 'hidden',
            minHeight: 0,
        },

        messagesContainer: {
            flex: 1,
            overflowY: 'auto',
            scrollbarGutter: isUpdateExperience ? 'stable' : undefined,
            padding: '10px 16px',
            display: 'flex',
            flexDirection: 'column',
            gap: isUpdateExperience ? '10px' : 0,
            minHeight: 0,
            selectors: {
                '> *': { flexShrink: 0 },
                // Only one auto margin: keep the transcript together without internal gaps.
                '> :first-child': { marginTop: 'auto' },
            },
        },

        messageRow: {
            display: 'flex',
            flexDirection: 'column',
            gap: '4px',
            alignItems: 'flex-start',
            selectors: isUpdateExperience ? undefined : { '& + &': { marginTop: 10 } },
        },

        botMessage: {
            alignSelf: 'flex-start',
            maxWidth: '100%',
            width: '100%',
        },

        userMessage: {
            alignSelf: 'flex-end',
            maxWidth: '85%',
        },

        messageContent: {
            padding: '4px 6px',
            borderRadius: '6px',
            backgroundColor: theme.palette.neutralLighter,
            fontSize: '14px',
            lineHeight: '24px',
            color: theme.palette.neutralPrimary,
        },

        suggestedPromptsContainer: {
            display: 'flex',
            flexDirection: 'column',
            gap: '8px',
            marginTop: '10px',
            width: '100%',
        },

        suggestedPromptsGrid: {
            display: 'none',
        },

        suggestedPromptButton: {
            width: '100%',
            minHeight: '32px',
            padding: isUpdateExperience ? '6px 8px' : '5px 8px',
            borderRadius: '10px',
            border: `1px solid ${theme.palette.neutralTertiaryAlt}`,
            backgroundColor: isUpdateExperience ? theme.palette.white : theme.palette.neutralLighterAlt,
            color: theme.palette.neutralPrimary,
            fontSize: '14px',
            fontWeight: 400,
            lineHeight: '20px',
            textAlign: 'left',
            cursor: 'pointer',
            transition: 'all 0.15s ease',
            boxShadow: isUpdateExperience ? '0 1px 2px rgba(0, 120, 212, 0.08)' : undefined,
            selectors: {
                ':hover': {
                    backgroundColor: theme.palette.themeLighterAlt,
                    borderColor: theme.palette.themePrimary,
                    boxShadow: '0 2px 6px rgba(0, 120, 212, 0.12)',
                },
                ':disabled': {
                    opacity: 0.5,
                    cursor: 'not-allowed',
                },
            },
        },

        welcomeCard: {
            padding: isUpdateExperience ? '4px 6px' : '12px 6px 10px',
            borderRadius: '6px',
            backgroundColor: theme.palette.neutralLighter,
            border: 'none',
            color: theme.palette.neutralPrimary,
            position: 'relative',
            overflow: 'hidden',
        },

        welcomeStepList: {
            display: 'flex',
            flexDirection: 'column',
            gap: '4px',
            marginTop: '10px',
        },

        welcomeStep: {
            display: 'flex',
            alignItems: 'flex-start',
            gap: '4px',
        },

        welcomeStepNumber: {
            width: '16px',
            height: '16px',
            marginTop: '4px',
            borderRadius: '50%',
            backgroundColor: theme.palette.themePrimary,
            border: 'none',
            color: theme.palette.white,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            fontSize: '11px',
            fontWeight: 600,
            flexShrink: 0,
        },

        welcomeStepText: {
            fontSize: '14px',
            lineHeight: '24px',
            color: theme.palette.neutralPrimary,
            flex: 1,
        },

        welcomeDisclaimer: {
            marginTop: '10px',
            padding: '8px',
            borderRadius: '10px',
            backgroundColor: theme.palette.white,
            color: theme.palette.neutralPrimary,
            fontSize: '10px',
            lineHeight: '14px',
        },

        inputContainer: {
            display: 'flex',
            gap: 0,
            padding: '10px 16px',
            borderTop: `1px solid ${theme.palette.neutralLight}`,
            backgroundColor: theme.palette.white,
            alignItems: 'flex-end',
            flexShrink: 0,
        },

        disclaimer: {
            padding: '0 16px 10px',
            backgroundColor: theme.palette.white,
            color: theme.palette.neutralSecondary,
            fontSize: '12px',
            lineHeight: '16px',
            textAlign: 'center',
            flexShrink: 0,
        },

        inputField: {
            flex: 1,
            minWidth: 0,
        },

        sendButton: {
            minWidth: '40px',
            height: '40px',
            padding: '0',
        },

        loadingIndicator: {
            display: 'flex',
            alignItems: 'center',
            gap: '8px',
            padding: '12px 16px',
            color: theme.palette.neutralSecondary,
            fontSize: '13px',
        },

        acceptChangesContainer: {
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            gap: '8px',
            padding: '16px 20px',
            borderTop: `1px solid ${theme.palette.neutralLight}`,
            backgroundColor: theme.palette.neutralLighterAlt,
        },

        acceptChangesButton: {
            width: '100%',
            backgroundColor: theme.palette.green,
            color: theme.palette.white,
            border: 'none',
            borderRadius: '4px',
            padding: '12px 20px',
            fontSize: '14px',
            fontWeight: 600,
            cursor: 'pointer',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            gap: '8px',
            transition: 'background-color 0.15s ease',
            selectors: {
                ':hover': {
                    backgroundColor: theme.palette.greenDark,
                },
            },
        },

        acceptChangesHint: {
            fontSize: '12px',
            color: theme.palette.neutralSecondary,
            textAlign: 'left',
        },

        generateFilterButton: {
            width: '100%',
            backgroundColor: theme.palette.themePrimary,
            color: theme.palette.white,
            border: 'none',
            borderRadius: '4px',
            padding: '12px 20px',
            fontSize: '14px',
            fontWeight: 600,
            cursor: 'pointer',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            gap: '8px',
            transition: 'background-color 0.15s ease',
            selectors: {
                ':hover': {
                    backgroundColor: theme.palette.themeDarkAlt,
                },
                ':disabled': {
                    backgroundColor: theme.palette.neutralLighter,
                    color: theme.palette.neutralTertiary,
                    cursor: 'not-allowed',
                },
            },
        },

        inlineAcceptButton: {
            width: '100%',
            height: 32,
            marginTop: 8,
            borderRadius: '4px',
        },
    };
};
