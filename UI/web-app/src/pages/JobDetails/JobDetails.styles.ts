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
        toggleRow: {
            display: "flex",
            alignItems: "center"
        },
        jobEnabled: {
            color: enabledTextColor,
            marginTop: 6,
            fontSize: 14,
            fontWeight: 400
        },
        jobDisabled: {
            color: theme.palette.neutralSecondary,
            marginTop: 6,
            fontSize: 14,
            fontWeight: 400
        },
        membershipStatusContainer: {
            display: "flex",
            flexDirection: "column",
            alignItems: "stretch"
        },
        membershipStatusHeader: {
            display: "flex",
            justifyContent: "space-between",
            alignItems: "flex-start",
            width: "100%"
        },
        membershipStatusControls: {
            display: "flex",
            flexDirection: "column",
            alignItems: "flex-start"
        },
        syncNowButton: {
            alignSelf: "flex-start"
        },
        pendingBanner: {
            display: "flex",
            alignItems: "center",
            gap: 8,
            padding: "10px 16px",
            borderRadius: 6,
            backgroundColor: theme.palette.neutralLighter
        },
        stickyBannerWrapper: {
            position: "sticky",
            top: 0,
            zIndex: 100,
            backgroundColor: theme.palette.neutralLighter,
            paddingBottom: 12
        },
        pendingBannerText: {
            fontSize: 14
        },
        pageHeaderRow: {
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            marginBottom: 12,
            paddingTop: 4
        },
        headerTitleGroup: {
            display: "flex",
            alignItems: "center",
            gap: 12
        },
        groupPill: {
            display: "inline-flex",
            alignItems: "center",
            gap: 8,
            backgroundColor: theme.palette.white,
            borderRadius: 20,
            padding: "3px 14px 3px 4px",
            boxShadow: "0 1px 2px rgba(0, 0, 0, 0.12)"
        },
        groupAvatar: {
            width: 26,
            height: 26,
            borderRadius: "50%",
            backgroundColor: theme.palette.themePrimary,
            color: theme.palette.white,
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            fontSize: 11,
            fontWeight: 600,
            flexShrink: 0
        },
        groupName: {
            fontWeight: 600,
            fontSize: 14
        },
        businessJustificationColumns: {
            display: "flex",
            gap: 40,
            alignItems: "flex-start",
            marginTop: 12
        },
        businessJustificationRequestedBy: {
            flex: "0 0 200px",
            minWidth: 160
        },
        requestedOnBehalfOf: {
            marginTop: 16
        },
        businessJustificationText: {
            flex: 1
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
        submissionReviewActions: {
            display: "flex",
            flexDirection: "row",
            justifyContent: "flex-end",
            gap: 10,
            marginTop: 16
        },
        membershipStatusPendingLabel: {
            display: "flex",
            gap: 10,
            marginBottom: 24
        },
        removeGMM: {
            display: "flex",
            alignItems: "center",
            justifyContent: "flex-end",
            paddingRight: 36,
            marginTop: 12,
        },
        removeGMMNotFound: {
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
