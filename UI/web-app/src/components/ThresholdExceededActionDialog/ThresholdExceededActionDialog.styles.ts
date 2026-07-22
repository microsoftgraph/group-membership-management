// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { NeutralColors } from '@fluentui/react';
import type { IThresholdExceededActionDialogStyleProps, IThresholdExceededActionDialogStyles } from './ThresholdExceededActionDialog.types';

export const getStyles = (props: IThresholdExceededActionDialogStyleProps): IThresholdExceededActionDialogStyles => {
    const { className, theme } = props;
    // The dark theme inverts only the neutral ramp (see theme/palette.ts), so accent-ramp slots like
    // themeLight/themeLighterAlt stay light and render poorly in dark mode. Use neutral-ramp surfaces
    // and a readable breach chip only in dark mode; keep light mode on the original accent colors.
    const isDarkMode = theme.palette.white.toLowerCase() === NeutralColors.gray220;

    return {
        root: [{
            padding: '0 0 24px 0',
        }, className],

        headerDivider: {
            height: 1,
            backgroundColor: theme.palette.neutralLight,
            marginTop: 12,
            marginBottom: 12,
            marginRight: -10,
        },

        sectionDivider: {
            height: 1,
            backgroundColor: theme.palette.neutralLight,
            marginTop: 20,
            marginBottom: 20,
        },

        errorMessageBar: {
            marginBottom: 12,
        },

        gracePeriodMessageBar: {
            marginBottom: 16,
            borderRadius: 4,
            overflow: 'hidden',
        },

        bodyLead: {
            color: theme.palette.neutralPrimary,
            fontSize: 14,
            marginTop: 0,
            marginBottom: 16,
        },

        aiDescriptionPanel: {
            backgroundColor: theme.palette.neutralLighter,
            borderRadius: 10,
            padding: '12px 16px',
            marginBottom: 16,
        },

        aiDescriptionLabel: {
            display: 'block',
            fontSize: 14,
            fontWeight: 600,
            color: theme.palette.neutralPrimary,
            marginBottom: 4,
        },

        aiDescriptionText: {
            fontSize: 14,
            color: theme.palette.neutralSecondary,
        },

        statsTable: {
            width: '100%',
            borderCollapse: 'collapse',
            marginBottom: 16,
            fontSize: 14,
            selectors: {
                'th, td': {
                    textAlign: 'left',
                    padding: '6px 12px',
                    borderBottom: `1px solid ${isDarkMode ? theme.palette.neutralLight : theme.palette.themeLight}`,
                },
                'thead th': {
                    borderBottom: `1px solid ${isDarkMode ? theme.palette.neutralLight : theme.palette.themeLight}`,
                },
            },
        },

        statsTableHeaderCell: {
            fontWeight: 600,
            color: theme.palette.neutralPrimary,
        },

        statsTableRowLabelCell: {
            fontWeight: 600,
            color: theme.palette.neutralPrimary,
        },

        statsTableLimitCell: {
            color: theme.palette.neutralPrimary,
        },

        statsTableActualCell: {
            color: theme.palette.neutralPrimary,
        },

        statsTableActualCellBreached: {
            display: 'inline-block',
            backgroundColor: isDarkMode ? theme.palette.themeLighter : theme.palette.themeLight,
            ...(isDarkMode ? { color: theme.palette.themeDarker } : {}),
            padding: '2px 8px',
            borderRadius: 4,
            fontWeight: 600,
        },

        statsTableBandedRow: {
            backgroundColor: isDarkMode ? theme.palette.neutralLighterAlt : theme.palette.themeLighterAlt,
        },

        disclosurePanel: {
            backgroundColor: theme.palette.neutralLighter,
            borderRadius: 10,
            padding: '12px 16px',
            marginBottom: 0,
        },

        disclosureHeader: {
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            width: '100%',
            padding: 0,
            border: 'none',
            background: 'transparent',
            cursor: 'pointer',
            fontSize: 14,
            fontWeight: 400,
            color: theme.palette.neutralPrimary,
            textAlign: 'left',
            selectors: {
                ':focus-visible': {
                    outline: `2px solid ${theme.palette.themePrimary}`,
                    outlineOffset: '2px',
                },
            },
        },

        disclosureChevron: {
            fontSize: 12,
            color: theme.palette.neutralSecondary,
            flexShrink: 0,
            marginLeft: 8,
        },

        disclosureBody: {
            marginTop: 12,
        },

        disclosureExplanation: {
            fontSize: 12,
            color: theme.palette.neutralSecondary,
            marginTop: 0,
            marginBottom: 12,
        },

        currentSettingsLabel: {
            fontSize: 14,
            fontWeight: 600,
            color: theme.palette.neutralPrimary,
            marginBottom: 8,
        },

        currentSettingsRow: {
            display: 'grid',
            gridTemplateColumns: '1fr 1fr',
            gap: 12,
            marginBottom: 16,
        },

        currentSettingCard: {
            display: 'flex',
            alignItems: 'flex-start',
            gap: 8,
            backgroundColor: theme.palette.white,
            border: `1px solid ${theme.palette.neutralLight}`,
            borderRadius: 10,
            padding: '12px',
        },

        currentSettingIcon: {
            fontSize: 16,
            color: theme.palette.themePrimary,
            flexShrink: 0,
            marginTop: 2,
        },

        currentSettingTitle: {
            fontWeight: 600,
            fontSize: 14,
            color: theme.palette.neutralPrimary,
        },

        currentSettingSubtitle: {
            fontSize: 12,
            color: theme.palette.neutralSecondary,
        },

        disclosureBottomRow: {
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            gap: 12,
        },

        disclosureInfoText: {
            display: 'flex',
            alignItems: 'flex-start',
            gap: 8,
            fontSize: 12,
            fontWeight: 400,
            color: theme.palette.neutralSecondary,
            selectors: {
                '.ms-Icon': {
                    fontSize: 14,
                    color: theme.palette.neutralSecondary,
                    flexShrink: 0,
                    marginTop: 2,
                },
            },
        },

        editThresholdsButton: {
            flexShrink: 0,
            borderRadius: 4,
        },

        chooseAnActionHeader: {
            fontSize: 16,
            fontWeight: 600,
            color: theme.palette.neutralPrimary,
            marginTop: 0,
            marginBottom: 12,
        },

        actionsGrid: {
            display: 'grid',
            gridTemplateColumns: '1fr 1fr',
            gap: 12,
        },

        actionCard: {
            display: 'flex',
            flexDirection: 'column',
            border: `1px solid ${theme.palette.neutralLight}`,
            borderRadius: 10,
            padding: '16px',
            backgroundColor: theme.palette.white,
        },

        actionCardTitleContainer: {
            display: 'flex',
            alignItems: 'center',
            gap: 8,
            marginBottom: 4,
        },

        actionCardIcon: {
            fontSize: 16,
            color: theme.palette.themePrimary,
            flexShrink: 0,
        },

        actionCardTitle: {
            fontWeight: 600,
            fontSize: 14,
            color: theme.palette.neutralPrimary,
        },

        actionCardDescription: {
            fontSize: 12,
            color: theme.palette.neutralSecondary,
            marginBottom: 16,
            flexGrow: 1,
        },

        actionCardButton: {
            alignSelf: 'flex-start',
            borderRadius: 4,
        },
    };
};
