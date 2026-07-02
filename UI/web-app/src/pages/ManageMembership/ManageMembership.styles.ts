// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    type IManageMembershipStyleProps,
    type IManageMembershipStyles,
} from './ManageMembership.types';

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
            paddingBottom: 24
        },
        circlesContainer: {
            flex: 1,
            display: 'flex',
            flexDirection: 'row',
            justifyContent: 'center',
            gap: 20
        },
        circleIcon: {
            color: theme.palette.themePrimary,
        },
        nextButtonContainer: {
            marginLeft: 20
        },
        nextButtonIcon: {
            marginLeft: 6,
            fontSize: 12
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


