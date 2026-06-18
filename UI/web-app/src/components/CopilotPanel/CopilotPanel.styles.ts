// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    type ICopilotPanelStyleProps,
    type ICopilotPanelStyles,
} from './CopilotPanel.types';
import { keyframes } from '@fluentui/react';

export const getStyles = (props: ICopilotPanelStyleProps): ICopilotPanelStyles => {
    const { className, theme } = props;

    const sparkleAnim1 = keyframes({
        '0%, 100%': { opacity: 1, transform: 'scale(1)' },
        '50%': { opacity: 0.2, transform: 'scale(0.5)' },
    });

    const sparkleAnim2 = keyframes({
        '0%, 100%': { opacity: 0.3, transform: 'scale(0.5)' },
        '50%': { opacity: 1, transform: 'scale(1)' },
    });

    return {
        root: [{
            display: 'flex',
            flexDirection: 'column',
            height: '100%',
        }, className],

        header: {
            padding: '14px 16px',
            borderBottom: `1px solid ${theme.palette.neutralLight}`,
            backgroundColor: theme.palette.white,
        },

        headerTitle: {
            fontSize: '16px',
            fontWeight: 600,
            color: theme.palette.themePrimary,
        },

        headerSubtitle: {
            fontSize: '12px',
            color: theme.palette.neutralSecondary,
        },

        sparkleStar1: {
            animationName: sparkleAnim1,
            animationDuration: '2s',
            animationTimingFunction: 'ease-in-out',
            animationIterationCount: 'infinite',
            transformOrigin: 'center',
            transformBox: 'fill-box',
        },

        sparkleStar2: {
            animationName: sparkleAnim2,
            animationDuration: '2s',
            animationTimingFunction: 'ease-in-out',
            animationIterationCount: 'infinite',
            transformOrigin: 'center',
            transformBox: 'fill-box',
        },

        chatContainer: {
            display: 'flex',
            flexDirection: 'column',
            flex: 1,
            overflow: 'hidden',
        },

        messagesContainer: {
            flex: 1,
            overflowY: 'auto',
            scrollbarGutter: 'stable',
            padding: '16px 20px',
            display: 'flex',
            flexDirection: 'column',
            gap: '16px',
        },

        messageRow: {
            display: 'flex',
            gap: '12px',
            alignItems: 'flex-start',
        },

        botMessage: {
            alignSelf: 'flex-start',
            maxWidth: '90%',
        },

        userMessage: {
            alignSelf: 'flex-end',
            maxWidth: '85%',
        },

        botAvatar: {
            width: '32px',
            height: '32px',
            borderRadius: '50%',
            backgroundColor: theme.palette.themePrimary,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            color: theme.palette.white,
            fontSize: '14px',
            fontWeight: 600,
            flexShrink: 0,
        },

        messageContent: {
            padding: '12px 16px',
            borderRadius: '8px',
            backgroundColor: theme.palette.neutralLighter,
            fontSize: '14px',
            lineHeight: '20px',
            color: theme.palette.neutralPrimary,
        },

        suggestedPromptsContainer: {
            display: 'flex',
            flexDirection: 'column',
            gap: '6px',
            marginTop: '10px',
            width: '100%',
        },

        suggestedPromptsHeader: {
            fontSize: '12px',
            fontWeight: 600,
            color: theme.palette.neutralSecondary,
            letterSpacing: '0.8px',
            textTransform: 'uppercase',
            marginBottom: '2px',
        },

        suggestedPromptsGrid: {
            display: 'none',
        },

        suggestedPromptButton: {
            width: '100%',
            minHeight: '34px',
            padding: '8px 14px',
            borderRadius: '12px',
            border: `1px solid #7ab8ff`,
            backgroundColor: theme.palette.white,
            color: theme.palette.themePrimary,
            fontSize: '12px',
            fontWeight: 500,
            lineHeight: '16px',
            textAlign: 'left',
            cursor: 'pointer',
            transition: 'all 0.15s ease',
            boxShadow: '0 1px 2px rgba(0, 120, 212, 0.08)',
            selectors: {
                ':hover': {
                    backgroundColor: '#f7fbff',
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
            padding: '14px 16px',
            borderRadius: '12px',
            backgroundColor: theme.palette.neutralLighterAlt,
            border: `1px solid ${theme.palette.neutralLight}`,
            color: theme.palette.neutralPrimary,
            position: 'relative',
            overflow: 'hidden',
        },

        welcomeStepList: {
            display: 'flex',
            flexDirection: 'column',
            gap: '6px',
            marginTop: '10px',
        },

        welcomeStep: {
            display: 'flex',
            alignItems: 'flex-start',
            gap: '10px',
        },

        welcomeStepNumber: {
            width: '20px',
            height: '20px',
            borderRadius: '50%',
            backgroundColor: '#0078d4',
            border: 'none',
            color: '#ffffff',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            fontSize: '11px',
            fontWeight: 600,
            flexShrink: 0,
        },

        welcomeStepText: {
            fontSize: '13px',
            lineHeight: '18px',
            color: theme.palette.neutralPrimary,
            flex: 1,
        },

        inputContainer: {
            display: 'flex',
            gap: '8px',
            padding: '16px 20px',
            borderTop: `1px solid ${theme.palette.neutralLight}`,
            backgroundColor: theme.palette.white,
            alignItems: 'flex-end',
        },

        inputField: {
            flex: 1,
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
            backgroundColor: theme.palette.green,
            color: theme.palette.white,
            border: 'none',
            borderRadius: '4px',
            padding: '8px 16px',
            fontSize: '13px',
            fontWeight: 600,
            cursor: 'pointer',
            display: 'inline-flex',
            alignItems: 'center',
            gap: '6px',
            transition: 'background-color 0.15s ease',
            alignSelf: 'flex-start',
            selectors: {
                ':hover': {
                    backgroundColor: theme.palette.greenDark,
                },
                ':disabled': {
                    backgroundColor: theme.palette.neutralLighter,
                    color: theme.palette.neutralTertiary,
                    cursor: 'not-allowed',
                },
            },
        },
    };
};
