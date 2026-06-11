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
            height: '100%',
            boxSizing: 'border-box',
            display: 'flex',
            flexDirection: 'column',
        },
        header: {
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
            gap: '12px',
            paddingBottom: '12px',
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
        statusCellContainer: {
            display: 'flex',
            flexDirection: 'column',
            gap: '2px',
        },
        statusCellThresholdExceeded: {
            color: theme.semanticColors.errorIcon,
        },
        syncFiltersContainer: {
            display: 'grid',
            gridTemplateColumns: '220px 1fr',
            gap: '12px',
            alignItems: 'start',
            marginBottom: '12px',
            overflow: 'visible',
        },
        statusFilter: {
            minWidth: '220px',
        },
        userSearchField: {
            width: '100%',
            position: 'relative',
            overflow: 'visible',
        },
        userSearchLabel: {
            display: 'block',
        },
        userSearchInputShell: {},
        userSearchPicker: {
            width: '100%',
        },
        userSearchPickerText: {},
        userSearchPickerItemsWrapper: {},
        userSearchIcon: {},
        userSuggestionList: {
            maxHeight: '240px',
            overflowY: 'auto',
            overflowX: 'hidden',
            padding: 0,
            selectors: {
                '&::-webkit-scrollbar': {
                    width: '8px',
                },
            },
        },
        userSuggestionItem: {
            selectors: {
                '& + &': {
                    borderTop: `1px solid ${theme.palette.neutralLighter}`,
                },
                '& .ms-Suggestions-itemButton': {
                    display: 'block',
                    width: '100%',
                    minHeight: 'auto',
                    height: 'auto',
                    padding: '8px 12px',
                    lineHeight: 'normal',
                    textAlign: 'left',
                    backgroundColor: 'transparent',
                },
                '& .ms-Button-flexContainer': {
                    display: 'flex',
                    alignItems: 'flex-start',
                    justifyContent: 'flex-start',
                    width: '100%',
                },
                '& .ms-Button-textContainer': {
                    display: 'block',
                    flexGrow: 1,
                    minWidth: 0,
                    overflow: 'visible',
                },
                '& .ms-Button-label': {
                    display: 'block',
                    width: '100%',
                    margin: 0,
                    overflow: 'visible',
                    textOverflow: 'clip',
                    whiteSpace: 'normal',
                    lineHeight: 'normal',
                },
                '&.is-suggested, &:hover': {
                    backgroundColor: theme.palette.neutralLighterAlt,
                },
                '&.is-suggested .ms-Suggestions-itemButton': {
                    backgroundColor: 'transparent',
                },
            },
        },
        userSuggestionRow: {
            display: 'flex',
            flexDirection: 'column',
            gap: '2px',
            width: '100%',
            minWidth: 0,
            padding: 0,
        },
        userSuggestionPrimaryText: {
            fontSize: '13px',
            lineHeight: '18px',
            fontWeight: 600,
            color: theme.palette.neutralPrimary,
            overflow: 'hidden',
            textOverflow: 'ellipsis',
            whiteSpace: 'nowrap',
        },
        userSuggestionSecondaryText: {
            fontSize: '12px',
            lineHeight: '16px',
            color: theme.palette.neutralSecondary,
            overflow: 'hidden',
            textOverflow: 'ellipsis',
            whiteSpace: 'nowrap',
        },
    };
};

