// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    type AdminConfigStyleProps,
    type AdminConfigStyles,
} from './AdminConfig.types';

export const getStyles = (props: AdminConfigStyleProps): AdminConfigStyles => {
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
            fontFamily: 'Segoe UI'
        },
        description: {
            padding: '10px 13px',
        },
        tiles: {
            display: 'flex',
            flexBasis: '100%',
            flexWrap: 'wrap',
            wrapFlow: 'row'
        },
        bottomContainer:{
            marginBottom: 24,
            display: 'flex',
            justifyContent: 'flex-end'
        }
    };
};


