// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    type MembershipConfigurationStyleProps,
    type MembershipConfigurationStyles,
} from './MembershipConfiguration.types';

export const getStyles = (props: MembershipConfigurationStyleProps): MembershipConfigurationStyles => {
    const { className, theme } = props;

    return {
        root: [{
        }, className],
        addButtonContainer: {
            paddingTop: 18,
            paddingBottom: 18,
            paddingLeft: 22,
            paddingRight: 22,
            borderRadius: 10,
            marginBottom: 12,
            backgroundColor: theme.palette.white
        },
        toggleContainer: {
            display: 'flex',
            flexDirection: 'row',
            justifyContent: 'flex-end',
        },
        card: {
            paddingTop: 18,
            paddingBottom: 18,
            paddingLeft: 22,
            paddingRight: 22,
            borderRadius: 10,
            marginBottom: 12,
            backgroundColor: theme.palette.white
        }
    };
};


