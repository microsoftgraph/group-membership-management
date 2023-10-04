// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    type IJobDetailsStyleProps,
    type IJobDetailsStyles,
} from './JobDetails.types';

export const getStyles = (props: IJobDetailsStyleProps): IJobDetailsStyles => {
    const { className, theme } = props;

    return {
        root: [{
            padding: '9px 36px 0px 36px'
        }, className],
        itemTitle:{
            fontSize: 14,
            fontWeight: 600
        },
        itemData: {
            paddingTop: 10,
            fontSize: 14
        },
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
            fontSize: 24,
            fontWeight: 600,
        },
        subtitle: {
            fontSize: 16,
            fontWeight: 600,
            marginTop: 10,
            display: 'flex',
            alignItems: 'center',
            gap: '4px'
        },
        toggleLabel: {
            paddingRight: 10
        },
        jobEnabled: {
            color: theme.palette.black,
            backgroundColor: theme.semanticColors.successBackground,
            borderRadius: 50,
            textAlign: 'center',
            height: 20,
            lineHeight: 20,
            paddingLeft: 5,
            paddingRight: 5,
            marginLeft: 15
        },
        jobDisabled: {
            color: theme.palette.black,
            backgroundColor: theme.palette.themeLighterAlt,
            borderRadius: 50,
            textAlign: 'center',
            height: 20,
            lineHeight: 20,
            paddingLeft: 5,
            paddingRight: 5,
            marginLeft: 15
        },
        membershipStatus: {
            display: "flex",
            alignItems: "flex-start"
        },
        footer: {
            display: 'flex',
            justifyContent: 'space-between'
        }
    };
};


