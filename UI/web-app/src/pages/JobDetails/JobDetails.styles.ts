// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    type IJobDetailsStyleProps,
    type IJobDetailsStyles,
} from './JobDetails.types';
import { NeutralColors } from '@fluentui/react';

export const getStyles = (props: IJobDetailsStyleProps): IJobDetailsStyles => {
    const { className, theme } = props;
    const isDarkMode = theme.palette.white.toLowerCase() === NeutralColors.gray220;
    const statusTextColor = theme.semanticColors.bodyText;
    const enabledTextColor = isDarkMode ? NeutralColors.white : statusTextColor;
    const enabledBackgroundColor = isDarkMode ? theme.palette.greenDark : theme.semanticColors.successBackground;
    const disabledBackgroundColor = theme.semanticColors.disabledBackground;

    return {
        root: [{
            padding: '0px 36px 0px 36px'
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
            color: enabledTextColor,
            backgroundColor: enabledBackgroundColor,
            borderRadius: 50,
            textAlign: 'center',
            height: 20,
            paddingLeft: 5,
            paddingRight: 5,
            marginLeft: 15,
            fontWeight: 600
        },
        jobDisabled: {
            color: statusTextColor,
            backgroundColor: disabledBackgroundColor,
            borderRadius: 50,
            textAlign: 'center',
            height: 20,
            paddingLeft: 5,
            paddingRight: 5,
            marginLeft: 15,
            fontWeight: 600
        },
        membershipStatusContainer: {
            display: "flex",
            alignItems: "flex-start"
        },
        membershipStatusControls: {
            display: "flex",
            alignItems: "flex-start"
        },
        membershipStatusMessage: {
            display: "flex",
            flexDirection: "column",
            paddingLeft: 50
        },
        requestor: {
            display: "flex",
            flexDirection: "column",
            paddingLeft: 50
        },
        hiddenMembershipWarningContainer: {
            display: 'flex',
            alignItems: 'center',
            gap: 8,
            marginBottom: 12,
            padding: '6px 10px',
            borderRadius: 4,
            backgroundColor: theme.semanticColors.errorBackground
        },
        hiddenMembershipWarningIcon: {
            color: theme.semanticColors.errorText
        },
        hiddenMembershipWarningText: {
            color: theme.semanticColors.errorText,
            fontWeight: 600
        },
        clockIcon: {
            color: theme.palette.yellowDark,
        },
        membershipStatusActionButtons:{
            display: "flex",
            flexDirection: "row",
            gap: 10,
            marginTop: 8
        },
        membershipStatusPendingLabel: {
            display: "flex",
            gap: 10,
            marginBottom: 24
        },
        removeGMM: {
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            marginTop: 20,
        },
        historyButtonContainer: {
            display: "flex",
            justifyContent: "flex-end"
        },
        userPersona: {
            height: 48,
            width: 48
        },
        notFound: {
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            justifyContent: 'center',
            textAlign: 'center',
            fontSize: '20px',
            padding: '9px 36px 0px 36px',
            maxWidth: '600px',
            width: '100%',
            margin: '0 auto',
            height: '20vh'
        }
    };
};
