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
            flexDirection: 'row'
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
        }
    };
};


