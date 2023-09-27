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
        bottomContainer:{
            marginBottom: 24,
            display: 'flex',
            justifyContent: 'flex-end'
        }
    };
};


