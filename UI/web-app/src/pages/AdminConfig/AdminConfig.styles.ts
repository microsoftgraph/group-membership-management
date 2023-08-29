// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    type IAdminConfigStyleProps,
    type IAdminConfigStyles,
} from './AdminConfig.types';

export const getStyles = (props: IAdminConfigStyleProps): IAdminConfigStyles => {
    const { className } = props;

    return {
        root: [{
        }, className],
        title: {
            fontWeight: 'bold',
            fontSize: '20px'
        },
        tiles: {
            display: 'flex'
        },
        bottomContainer:{
            display: 'flex',
            justifyContent: 'flex-end'
        }
    };
};


