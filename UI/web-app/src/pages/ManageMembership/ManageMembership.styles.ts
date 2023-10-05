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
        card: {
            paddingTop: 18,
            paddingBottom: 18,
            paddingLeft: 22,
            paddingRight: 22,
            borderRadius: 10,
            marginBottom: 12,
            backgroundColor: theme.palette.white
        },
        title: {
            fontWeight: 600,
            fontSize: 24,
            fontFamily: 'Segoe UI',
            marginBottom: 14
        },
        stepTitle: {
            fontWeight: 600,
            fontSize: 20,
            fontFamily: 'Segoe UI',
            marginBottom: 8
        },
        stepDescription: {
            fontWeight: 400,
            fontSize: 16,
            fontFamily: 'Segoe UI'
        },
        dropdownTitle: {
            borderRadius: 4,
            borderStyle: 'solid',
            borderWidth: 1,
            borderColor: theme.palette.neutralQuaternary,
            background: theme.palette.white,
            width: 500
        },
        dropdownField: {
            width: 500
        },
        searchField: {
            width: 500,
            borderRadius: 4,
            borderStyle: 'solid',
            borderWidth: 1,
            borderColor: theme.palette.neutralQuaternary,
            selectors: {
                '&::after': {
                    borderColor: theme.palette.neutralQuaternary,
                    content: 'none'
                }
            }
        },
        comboBoxContainer: {
            width: 500
        },
        comboBoxInput: {
            width: '100%'
        },
        ownershipWarning:{
            color: theme.semanticColors.errorText
        },
        bottomContainer: {
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
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
        endpointsContainer: {
            display: 'flex',
            flexDirection: 'column'
        },
        outlookWarning: {
            width: 'fit-content',
        },
        outlookContainer:{
            display: 'flex',
            flexDirection: 'row',
            gap: 70
        }
    };
};


