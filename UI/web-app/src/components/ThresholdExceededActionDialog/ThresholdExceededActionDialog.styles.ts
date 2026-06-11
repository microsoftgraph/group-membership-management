// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import type { IThresholdExceededActionDialogStyleProps, IThresholdExceededActionDialogStyles } from './ThresholdExceededActionDialog.types';

export const getStyles = (props: IThresholdExceededActionDialogStyleProps): IThresholdExceededActionDialogStyles => {
    const { className, theme } = props;

    return {
        root: [{
            padding: '0 24px 24px 24px',
        }, className],

        warningText: {
            color: theme.semanticColors.errorText,
            fontSize: 14,
            marginBottom: 8,
        },

        detailsText: {
            color: theme.palette.neutralPrimary,
            fontSize: 14,
            marginBottom: 20,
        },

        actionsGrid: {
            display: 'grid',
            gridTemplateColumns: '1fr 1fr',
            gap: 12,
            marginBottom: 24,
        },

        actionCard: {
            border: `1px solid ${theme.palette.neutralLight}`,
            borderRadius: 4,
            padding: '16px',
            cursor: 'default',
            backgroundColor: theme.palette.white,
        },

        actionCardEnabled: {
            border: `1px solid ${theme.palette.neutralLight}`,
            borderRadius: 4,
            padding: '16px',
            cursor: 'pointer',
            backgroundColor: theme.palette.white,
            selectors: {
                ':hover': {
                    backgroundColor: theme.palette.neutralLighterAlt,
                    borderColor: theme.palette.neutralTertiary,
                },
                ':focus': {
                    outline: `2px solid ${theme.palette.themePrimary}`,
                    outlineOffset: '-2px',
                },
            },
        },

        actionCardTitle: {
            fontWeight: 600,
            fontSize: 14,
            marginBottom: 4,
            color: theme.palette.neutralPrimary,
        },

        actionCardDescription: {
            fontSize: 12,
            color: theme.palette.neutralSecondary,
        },

        footer: {
            display: 'flex',
            justifyContent: 'flex-end',
        },
    };
};
