// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    type IJobHistoryPanelStyleProps,
    type IJobHistoryPanelStyles,
} from './JobHistoryPanel.types';

export const getStyles = (props: IJobHistoryPanelStyleProps): IJobHistoryPanelStyles => {
    const { className, theme } = props;

    return {
        root: [{}, className],

        container: {
            padding: 20,
        },
        header: {
            display: 'flex',
            justifyContent: 'flex-end',
        },
        dateTimeContainer: {
            display: 'flex',
            flexDirection: 'column',
            gap: '2px',
        },
        dateText: {
            fontWeight: '600',
            fontSize: '14px',
        },
        timeText: {
            fontSize: '12px',
            color: theme.palette.neutralSecondary,
        },
        changeReasonContainer: {
            display: 'flex',
            alignItems: 'center',
            gap: '8px',
        },
        changeTypeIndicator: {
            width: '12px',
            height: '12px',
            borderRadius: '2px',
            flexShrink: 0,
        },
        changeTypeRejected: {
            backgroundColor: theme.semanticColors.errorIcon,
        },
        changeTypeApproved: {
            backgroundColor: theme.semanticColors.successIcon,
        },
        changeTypeUpdate: {
            backgroundColor: theme.palette.themePrimary,
        },
        changeTypeGroupSettings: {
            backgroundColor: theme.palette.themePrimary,
        },
        changeTypeDefault: {
            backgroundColor: theme.palette.neutralSecondary,
        },
    };
};

