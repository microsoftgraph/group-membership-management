// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    type MembershipConfigurationStyleProps,
    type MembershipConfigurationStyles,
} from './MembershipConfiguration.types';
import { isDarkTheme } from '../../theme/accessibleColors';

export const getStyles = (props: MembershipConfigurationStyleProps): MembershipConfigurationStyles => {
    const { className, theme } = props;
    const surfaceBackground = isDarkTheme(theme) ? theme.palette.neutralLighterAlt : theme.palette.white;

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
            backgroundColor: surfaceBackground
        },
        card: {
            marginBottom: 12
        },
        expandCollapseButton: {
            display: 'flex',
            flexDirection: 'row',
            justifyContent: 'flex-end',
        }
    };
};

