// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { IButtonStyles, ITheme } from '@fluentui/react';
import {
    type IManageMembershipStyleProps,
    type IManageMembershipStyles,
} from './ManageMembership.types';

// Secondary nav button (Previous/Next Step) styling per design spec:
// height 32 (hug), radius 4, border 1px, padding 5px/12px, 4px icon/text gap.
// Uses theme semantic colors so it stays responsive to theme/high-contrast changes,
// and only grays out the text/icon when disabled (border/background stay the same).
export const getNavButtonStyles = (theme: ITheme, reverseIconOrder?: boolean): IButtonStyles => ({
    root: {
        height: 32,
        width: 150,
        borderRadius: 4,
        border: `1px solid ${theme.semanticColors.variantBorder}`,
        backgroundColor: theme.semanticColors.bodyBackground,
        padding: '5px 12px',
        selectors: {
            '.ms-Button-flexContainer': { gap: 4, justifyContent: 'center', flexWrap: 'nowrap' },
            '.ms-Button-label': { whiteSpace: 'nowrap', margin: 0, order: reverseIconOrder ? 1 : undefined },
            ...(reverseIconOrder ? {
                '.ms-Button-icon': { order: 2 },
            } : {}),
        },
    },
    rootHovered: {
        border: `1px solid ${theme.semanticColors.variantBorder}`,
        backgroundColor: theme.semanticColors.bodyBackgroundHovered,
    },
    rootPressed: {
        border: `1px solid ${theme.semanticColors.variantBorder}`,
        backgroundColor: theme.semanticColors.bodyBackgroundChecked,
    },
    rootDisabled: {
        border: `1px solid ${theme.semanticColors.variantBorder}`,
        backgroundColor: theme.semanticColors.bodyBackground,
    },
    label: {
        color: theme.semanticColors.bodyText,
    },
    labelDisabled: {
        color: theme.semanticColors.disabledText,
    },
    icon: {
        color: theme.semanticColors.bodyText,
    },
    iconDisabled: {
        color: theme.semanticColors.disabledText,
    },
});

export const getStyles = (props: IManageMembershipStyleProps): IManageMembershipStyles => {
    const { className, theme } = props;

    return {
        root: [{
            padding: '9px 36px 0px 36px'
        }, className],

        bottomContainer: {
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
            flexDirection: 'row',
            paddingBottom: 24,
            position: 'relative',
        },
        circlesContainer: {
            position: 'absolute',
            left: '50%',
            top: '50%',
            transform: 'translate(-50%, -50%)',
            display: 'flex',
            flexDirection: 'row',
            justifyContent: 'center',
            alignItems: 'center',
        },
        stepIndicatorText: {
            fontFamily: 'Segoe UI',
            fontWeight: 400,
            fontSize: 14,
            lineHeight: '20px',
            letterSpacing: '0%',
            color: theme.palette.neutralPrimary,
        },
        nextButtonContainer: {
            marginLeft: 20
        },
        backButtonContainer: {
            marginRight: 20
        },
        overlay: {
            position: 'fixed',
            top: 0,
            left: 0,
            right: 0,
            bottom: 0,
            backgroundColor: 'rgba(0,0,0,0.5)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            zIndex: 1000
        },
        pageHeaderRow: {
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
            marginBottom: 12,
            paddingTop: 4
        },
        headerTitleGroup: {
            display: 'flex',
            alignItems: 'center',
            gap: 12
        },
        pageTitle: {
            fontWeight: 600,
            fontSize: 24,
            fontFamily: 'Segoe UI',
            color: theme.palette.neutralPrimary
        },
        groupPill: {
            display: 'inline-flex',
            alignItems: 'center',
            gap: 8,
            backgroundColor: theme.palette.white,
            borderRadius: 20,
            padding: '3px 14px 3px 4px',
            boxShadow: '0 1px 2px rgba(0, 0, 0, 0.12)'
        },
        groupAvatar: {
            width: 26,
            height: 26,
            borderRadius: '50%',
            backgroundColor: theme.palette.themePrimary,
            color: theme.palette.white,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            fontSize: 11,
            fontWeight: 600
        },
        groupName: {
            fontWeight: 600,
            fontSize: 14
        }
    };
};


