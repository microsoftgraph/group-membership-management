// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
  type IJobsListStyleProps,
  type IJobsListStyles,
} from './JobsList.types';
import { NeutralColors } from '@fluentui/react';

export const getStyles = (props: IJobsListStyleProps): IJobsListStyles => {
  const { className, theme } = props;
  const isDarkMode = theme.palette.white.toLowerCase() === NeutralColors.gray220;
  const statusTextColor = theme.semanticColors.bodyText;
  const enabledTextColor = isDarkMode ? NeutralColors.white : statusTextColor;
  const enabledBackgroundColor = isDarkMode ? theme.palette.greenDark : theme.semanticColors.successBackground;
  const disabledBackgroundColor = theme.semanticColors.disabledBackground;

  return {
    root: [{
      margin: '0px 36px 12px 36px'
    }, className],
    enabled: {
      color: enabledTextColor,
      backgroundColor: enabledBackgroundColor,
      borderRadius: 50,
      textAlign: 'center',
      height: 20,
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      fontWeight: 600
    },
    disabled: {
      color: statusTextColor,
      backgroundColor: disabledBackgroundColor,
      borderRadius: 50,
      textAlign: 'center',
      height: 20,
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      fontWeight: 600
    },
    actionRequiredIcon: {
      color: theme.semanticColors.errorIcon,
    },
    pendingReviewIcon: {
      color: theme.palette.yellowDark,
    },
    rejectedIcon: {
      color: theme.semanticColors.disabledText,
    },
    titleContainer: {
      display: 'flex',
      justifyContent: 'space-between',
    },
    title: {
      paddingLeft: 9
    },
    tabContent: {
      cursor: 'pointer',
      display: 'flex',
      flexDirection: 'column',
      paddingLeft: 6
    },
    refresh: {
      padding: 22.5,
    },
    jobsList: {
      backgroundColor: theme.palette.white,
      borderRadius: 10,
      padding: '12px 24px 12px 24px',
    },
    jobsListFilter: {
      marginBottom: 12,
    },
    footer: {
      display: 'flex-end'
    },
    noMembershipsFoundText: {
      textAlign: 'center',
      padding: 24
    },
    errorMessageBar: {
      borderRadius: 5,
      marginBottom: 22,
    },
    header: {
      display: 'flex',
      marginBottom: '10px'
    },
    manageMembershipButton: {
        // Ensure dropdown button displays properly when right-aligned
        display: 'inline-block',
        // Align dropdown menu with right edge of button
        '& .ms-ContextualMenu': {
            right: 0,
            left: 'auto'
        }
    },
    successStatus: {
      color: theme.palette.green,
      fontSize: 20,
      verticalAlign: 'middle'
    },
    errorStatus: {
      color: theme.palette.red,
      fontSize: 20,
      verticalAlign: 'middle'
    },
    chooseFileButton: {
      color: theme.semanticColors.link,
      cursor: "pointer",
      textDecoration: "underline",
      ':hover': {
        color: theme.semanticColors.linkHovered,
      }
    },
    jobsHeader: {
      display: 'flex',
      marginBottom: '10px'
    },
    approvedJobsLabel: {
      width: '150px',
      fontWeight: 'bold',
      fontSize: 20
    },
    totalJobsLabel: {
      marginLeft: '20px',
      fontWeight: 'bold',
      fontSize: 20
    }
  };
};
