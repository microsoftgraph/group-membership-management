// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    type IMaintenanceStyleProps,
    type IMaintenanceStyles,
} from './Maintenance.types';

export const getStyles = (props: IMaintenanceStyleProps): IMaintenanceStyles => {
    const { className, theme } = props;

    return {
        root: [{
            padding: '20px',
            textAlign: 'center',
            display: 'flex',
            flexDirection: 'column',
            justifyContent: 'center',
            alignItems: 'center',
            minHeight: '100vh',
        }, className],
        container: {
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
        },
        icon: {
            fontSize: '48px',
            color: theme.palette.neutralSecondary,
            marginBottom: '16px',
        },
        title: {
            display: 'block',
            marginBottom: '8px',
        },
        message: {
            color: theme.palette.neutralSecondary,
        }
    };
};
