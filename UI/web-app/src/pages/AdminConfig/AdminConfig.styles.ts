// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    type AdminConfigStyleProps,
    type AdminConfigStyles,
} from './AdminConfig.types';
import { getAccessibleStatusColors, isDarkTheme } from '../../theme/accessibleColors';

export const getStyles = (props: AdminConfigStyleProps): AdminConfigStyles => {
    const { className, theme } = props;
    const isDarkMode = isDarkTheme(theme);
    const statusColors = getAccessibleStatusColors(theme);

    return {
        root: [{
            padding: '9px 36px 0px 36px'
        }, className],
        card: {
            paddingTop: 18,
            paddingBottom: 18,
            paddingLeft: 22,
            paddingRight: 22,
            borderRadius: 10,
            marginBottom: 12,
            backgroundColor: theme.palette.white
        },
        titleRow: {
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            gap: 16,
            padding: '4px 4px 14px 4px'
        },
        saveButton: {
            borderRadius: 4,
            minWidth: 80,
        },
        title: {
            fontWeight: 600,
            fontSize: 28,
            lineHeight: '36px',
            color: theme.palette.neutralPrimary,
            fontFamily: 'Segoe UI'
        },
        description: {
            padding: '10px 13px',
        },
        tiles: {
            display: 'grid',
            gridTemplateColumns: 'repeat(2, minmax(0, 1fr))',
            gap: 16,
            alignItems: 'start',
            selectors: {
                '@media (max-width: 900px)': {
                    gridTemplateColumns: 'minmax(0, 1fr)',
                },
            },
        },
        customLabelTextField: {
            borderRadius: 4,
            border: '1px solid',
            borderColor: theme.palette.neutralQuaternary,
            background: theme.palette.white,
            width: '100%'
        },
        defaultColumnSpan: {
            display: 'flex',
            alignItems: 'center',
            textAlign: 'center',
            height: 30,
            color: theme.palette.neutralPrimary
        },
        sourceNameTextFieldContainer: {
            maxWidth: 370
        },
        sourceNameTextField: {
            borderRadius: 4,
            border: '1px solid',
            borderColor: theme.palette.neutralQuaternary,
            background: theme.palette.white,
            width: '100%',
        },
        detailsListContainer: {
            marginTop: 4,
            height: 700,
            overflowY: 'auto'
        },
        descriptionText: {
            fontWeight: 400,
            color: theme.palette.black
        },
        valuesDropdown: {
            width: '100%'
        },
        valuesDropdownTitle: {
            borderRadius: 4,
            borderStyle: 'solid',
            borderWidth: 1,
            borderColor: theme.palette.neutralQuaternary,
            backgroud: theme.palette.white,
            width: '100%'
        },
        valuesDropdownSpinner: {
            marginTop: 10,
            marginBottom: 10
        },
        descriptionTextField: {
            width: '100%',
            borderRadius: 4,
            border: '1px solid',
            borderColor: theme.palette.neutralQuaternary,
            background: theme.palette.white,
        },
        sensitiveHintIcon: {
            fontSize: 14,
            color: theme.palette.themePrimary,
            verticalAlign: 'text-bottom',
            marginRight: 6
        },
        sensitiveSummarySeparator: {
            margin: '0 6px',
            color: theme.palette.neutralTertiary
        },
        sensitiveSummary: {
            fontSize: 12,
            lineHeight: '18px',
            marginBottom: 12
        },
        screeningRow: {
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            gap: 12,
            fontSize: 13,
            paddingLeft: 16,
            selectors: { '.ms-Icon': { marginLeft: 6 } }
        },
        aiSettingsLeaveEmptyNote: {
            display: 'flex',
            alignItems: 'center',
            gap: 6,
            marginBottom: 12,
            color: theme.palette.neutralSecondary,
        },
        aiSettingsLeaveEmptyNoteIcon: {
            fontSize: 14,
        },
        aiSettingsDefaultInstructionsContainer: {
            marginBottom: 12,
            border: `1px solid ${theme.palette.neutralLighter}`,
            borderRadius: 4,
            overflow: 'hidden',
        },
        aiSettingsDefaultInstructionsToggle: {
            width: '100%',
            padding: '10px 14px',
            display: 'flex',
            alignItems: 'center',
            gap: 8,
            background: theme.palette.neutralLighterAlt,
            border: 'none',
            cursor: 'pointer',
            fontSize: 13,
            fontWeight: 600,
            color: theme.palette.neutralPrimary,
        },
        aiSettingsDefaultInstructionsToggleIcon: {
            fontSize: 12,
        },
        aiSettingsDefaultInstructionsContent: {
            padding: '12px 14px',
            backgroundColor: theme.palette.neutralLighter,
            whiteSpace: 'pre-wrap',
            fontSize: 12,
            fontFamily: 'Consolas, monospace',
            color: theme.palette.neutralPrimary,
            maxHeight: 300,
            overflowY: 'auto',
        },
        sectionContainer: {
            marginTop: 32,
            selectors: {
                ':first-child': {
                    marginTop: 16,
                },
            },
        },
        sectionContainerDivided: {
            marginTop: 32,
            paddingTop: 32,
            borderTop: `1px solid ${theme.palette.neutralLight}`,
        },
        modelBehaviorCard: {
            display: 'flex',
            flexDirection: 'column',
            gap: 12,
            padding: 24,
            borderRadius: 8,
            boxSizing: 'border-box',
            border: `1px solid ${theme.palette.neutralLight}`,
            backgroundColor: theme.palette.white,
        },
        modelBehaviorHeader: {
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            gap: 16,
        },
        modelBehaviorTitle: {
            fontWeight: 600,
            whiteSpace: 'nowrap',
        },
        modelBehaviorSlider: {
            flexGrow: 1,
            minWidth: 0,
            maxWidth: 260,
        },
        modelBehaviorDescription: {
            color: theme.palette.neutralPrimary,
            lineHeight: '20px',
        },
        sectionHeading: {
            display: 'block',
            fontWeight: 600,
            fontSize: 14,
            lineHeight: '20px',
            marginBottom: 4,
            textTransform: 'uppercase',
            color: theme.palette.themePrimary,
        },
        sectionSubtitle: {
            display: 'block',
            marginTop: 0,
            marginBottom: 16,
            lineHeight: '20px',
            color: theme.palette.neutralPrimary,
        },
        settingsGrid: {
            display: 'grid',
            gridTemplateColumns: 'repeat(2, minmax(0, 1fr))',
            gap: 16,
            alignItems: 'stretch',
            selectors: {
                '@media (max-width: 900px)': {
                    gridTemplateColumns: 'minmax(0, 1fr)',
                },
            },
        },
        suggestedPromptsGrid: {
            display: 'grid',
            gridTemplateColumns: 'repeat(2, minmax(0, 1fr))',
            gap: 12,
            selectors: {
                '@media (max-width: 900px)': {
                    gridTemplateColumns: 'minmax(0, 1fr)',
                },
            },
        },
        suggestedPromptRow: {
            display: 'flex',
            gap: 8,
            alignItems: 'flex-start',
            minWidth: 0,
        },
        suggestedPromptLabelField: {
            flex: '1 1 0',
            minWidth: 0,
        },
        suggestedPromptPromptField: {
            flex: '2 1 0',
            minWidth: 0,
        },
        suggestedPromptRemoveButton: {
            flexShrink: 0,
            color: theme.palette.themePrimary,
        },
        suggestedPromptsActions: {
            display: 'flex',
            gap: 8,
            marginTop: 8,
            alignItems: 'center',
        },
        suggestedPromptAddButton: {
            borderRadius: 4,
            borderColor: theme.palette.neutralQuaternary,
        },
        operationsGrid: {
            display: 'grid',
            gridTemplateColumns: 'repeat(2, minmax(0, 1fr))',
            gap: 16,
            alignItems: 'stretch',
            selectors: {
                '@media (max-width: 900px)': {
                    gridTemplateColumns: 'minmax(0, 1fr)',
                },
            },
        },
        serviceNotificationCard: {
            borderRadius: 10,
            backgroundColor: theme.palette.white,
            outline: `1px solid ${theme.palette.neutralQuaternary}`,
            width: '100%',
            minWidth: 0,
            boxSizing: 'border-box',
            padding: 24,
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'stretch',
            gap: 4,
        },
        serviceNotificationTitle: {
            fontWeight: 600,
            fontSize: 16,
        },
        serviceNotificationToggle: {
            marginTop: 12,
            marginBottom: 0,
        },
        serviceNotificationFieldRow: {
            display: 'grid',
            gridTemplateColumns: 'repeat(2, minmax(0, 1fr))',
            gap: 16,
            alignItems: 'start',
            selectors: {
                '@media (max-width: 900px)': {
                    gridTemplateColumns: 'minmax(0, 1fr)',
                },
            },
        },
        serviceNotificationTextFieldGroup: {
            borderRadius: 4,
            border: '1px solid',
            borderColor: theme.palette.neutralQuaternary,
            background: theme.palette.white,
            width: '100%',
        },
        serviceNotificationErrorMessage: {
            marginTop: 8,
        },
        serviceNotificationInfoIcon: {
            fontSize: 14,
            color: theme.palette.themePrimary,
        },
        dashboardRoot: {
            display: 'flex',
            flexDirection: 'column',
            gap: 12,
            minWidth: 0,
            marginTop: 24,
        },
        dashboardHeader: {
            display: 'flex',
            flexWrap: 'wrap',
            alignItems: 'center',
            justifyContent: 'space-between',
            gap: 12,
        },
        dashboardHeading: {
            margin: 0,
            fontSize: 14,
            fontWeight: 600,
            lineHeight: '20px',
            textTransform: 'uppercase',
            color: statusColors.accentText,
        },
        dashboardToolbar: {
            display: 'flex',
            flexWrap: 'wrap',
            alignItems: 'center',
            justifyContent: 'flex-end',
            gap: 8,
            minWidth: 0,
            maxWidth: '100%',
        },
        dashboardRefresh: {
            display: 'flex',
            alignItems: 'center',
            gap: 4,
            minWidth: 0,
            maxWidth: '100%',
        },
        dashboardSection: {
            display: 'flex',
            flexDirection: 'column',
            gap: 12,
            minWidth: 0,
            padding: 18,
            borderRadius: 8,
            border: `1px solid ${theme.palette.neutralQuaternary}`,
            backgroundColor: theme.palette.white,
            boxSizing: 'border-box',
        },
        dashboardSectionRow: {
            display: 'grid',
            gridTemplateColumns: 'repeat(2, minmax(0, 1fr))',
            gap: 12,
            alignItems: 'stretch',
            '@media (max-width: 1050px)': {
                gridTemplateColumns: 'minmax(0, 1fr)',
            },
        },
        dashboardCardHeader: {
            display: 'flex',
            flexWrap: 'wrap',
            alignItems: 'center',
            justifyContent: 'space-between',
            gap: 8,
        },
        dashboardCardTitle: {
            display: 'flex',
            alignItems: 'center',
            gap: 4,
        },
        dashboardInfoIcon: {
            fontSize: 12,
            color: theme.palette.neutralPrimary,
            cursor: 'help',
        },
        dashboardSnapshot: {
            fontSize: 12,
            color: theme.palette.neutralSecondary,
        },
        dashboardTable: {
            minWidth: 0,
            maxWidth: '100%',
            overflowX: 'auto',
            selectors: {
                '.ms-DetailsHeader': {
                    paddingTop: 0,
                    borderBottomColor: theme.palette.neutralTertiaryAlt,
                },
                '.ms-DetailsHeader-cellTitle': {
                    fontWeight: 400,
                },
                '.ms-DetailsRow': {
                    borderBottom: `1px solid ${theme.palette.neutralLight}`,
                    color: theme.palette.neutralPrimary,
                },
            },
        },
        dashboardBreakdown: {
            marginTop: 4,
            paddingTop: 12,
            borderTop: `1px solid ${theme.palette.neutralLight}`,
            selectors: {
                summary: {
                    cursor: 'pointer',
                    color: statusColors.accentText,
                    fontSize: 12,
                },
            },
        },
        dashboardHint: {
            display: 'block',
            margin: 0,
            fontSize: 12,
            lineHeight: '20px',
            color: theme.palette.neutralPrimary,
        },
        dashboardEmpty: {
            display: 'block',
            padding: '8px 0',
            color: theme.palette.neutralSecondary,
        },
        dashboardCounts: {
            display: 'flex',
            flexWrap: 'wrap',
            gap: 8,
        },
        dashboardCount: {
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            flex: '1 1 100px',
            gap: 6,
            minWidth: 0,
            padding: '5px 8px',
            borderRadius: 5,
            borderLeftWidth: 4,
            borderLeftStyle: 'solid',
            backgroundColor: isDarkMode ? theme.palette.neutralLight : theme.palette.neutralLighter,
            boxSizing: 'border-box',
        },
        dashboardHeadlineCount: {
            flexDirection: 'column',
            gap: 2,
            padding: '8px 12px',
        },
        dashboardCountLabel: {
            fontSize: 14,
            lineHeight: '20px',
            color: theme.palette.neutralPrimary,
            minWidth: 0,
            maxWidth: '100%',
            overflow: 'hidden',
            textOverflow: 'ellipsis',
            whiteSpace: 'nowrap',
        },
        dashboardCountValue: {
            fontSize: 14,
            fontWeight: 600,
            lineHeight: '20px',
            color: theme.palette.neutralPrimary,
            flexShrink: 0,
        },
    };
};
