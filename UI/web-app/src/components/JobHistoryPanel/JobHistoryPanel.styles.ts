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

        headerDivider: {
            height: 1,
            backgroundColor: theme.palette.neutralLight,
            marginTop: 12,
            marginBottom: 12,
            marginRight: -10,
        },
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
            fontWeight: 600,
            fontSize: '14px',
        },
        statusCellThresholdApproved: {
            color: theme.semanticColors.successIcon,
            fontWeight: 600,
        },
        syncFiltersContainer: {
            display: 'grid',
            gridTemplateColumns: '260px 260px 1fr',
            gap: '16px',
            alignItems: 'end',
            marginBottom: '12px',
            overflow: 'visible',
        },
        statusFilter: {
            minWidth: '220px',
        },
        eventTypeFilterField: {
            minWidth: 260,
        },
        filterActionsBar: {
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'flex-end',
            paddingBottom: '4px',
        },
        clearFiltersLink: {
            display: 'inline-flex',
            alignItems: 'center',
            gap: '4px',
            fontSize: '13px',
            color: theme.palette.themePrimary,
            cursor: 'pointer',
            background: 'none',
            border: 'none',
            padding: '4px 0',
            textDecoration: 'none',
            selectors: {
                ':hover': {
                    color: theme.palette.themeDarker,
                    textDecoration: 'none',
                },
                ':focus-visible': {
                    outline: `1px solid ${theme.palette.themePrimary}`,
                    outlineOffset: '2px',
                },
            },
        },
        clearFiltersLinkDisabled: {
            color: theme.palette.neutralTertiary,
            cursor: 'default',
            selectors: {
                ':hover': {
                    color: theme.palette.neutralTertiary,
                    textDecoration: 'none',
                },
            },
        },
        clearFiltersIconWrapper: {
            display: 'inline-flex',
            alignItems: 'center',
            justifyContent: 'center',
            width: '24px',
            height: '20px',
            flexShrink: 0,
        },
        clearFiltersBaseIcon: {},
        clearFiltersBadgeIcon: {},
        userSearchField: {
            width: '100%',
            position: 'relative',
            overflow: 'visible',
        },
        userSearchLabel: {
            display: 'block',
        },
        userSearchInputWrapper: {
            position: 'relative',
            width: '100%',
            selectors: {
                '& .ms-BasePicker': {
                    width: '100%',
                },
                '& .ms-BasePicker-text': {
                    width: '100%',
                    minWidth: 0,
                    paddingRight: '32px',
                },
            },
        },
        userSearchInputShell: {},
        userSearchPicker: {
            width: '100%',
        },
        userSearchPickerText: {},
        userSearchPickerItemsWrapper: {},
        userSearchIcon: {},
        userSearchTrailingIcon: {
            position: 'absolute',
            right: '8px',
            top: '50%',
            transform: 'translateY(-50%)',
            fontSize: '16px',
            color: theme.palette.neutralSecondary,
            pointerEvents: 'none',
        },
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
        highlightedAddedCell: {
            fontWeight: 700,
            color: theme.semanticColors.successIcon,
            backgroundColor: theme.semanticColors.successBackground,
            borderRadius: '10px',
            padding: '2px 8px',
            display: 'inline-block',
        },
        highlightedRemovedCell: {
            fontWeight: 700,
            color: theme.semanticColors.errorIcon,
            backgroundColor: theme.semanticColors.errorBackground,
            borderRadius: '10px',
            padding: '2px 8px',
            display: 'inline-block',
        },
        pendingCell: {
            display: 'inline-flex',
            flexDirection: 'column',
            alignItems: 'center',
            gap: '1px',
            width: 'fit-content',
            padding: '2px 4px',
            borderRadius: '4px',
            backgroundColor: theme.palette.neutralLighter,
            border: `1px solid ${theme.palette.neutralQuaternaryAlt}`,
            color: theme.palette.neutralPrimary,
        },
        pendingMarker: {
            display: 'inline-block',
            fontSize: '11px',
            lineHeight: '14px',
            fontWeight: 400,
            color: theme.palette.neutralSecondary,
        },
        matchingRow: {
            backgroundColor: theme.palette.themeLighterAlt,
        },
        userSearchBanner: {
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            justifyContent: 'center',
            padding: '16px 20px',
            marginBottom: '8px',
            backgroundColor: theme.palette.themeLighterAlt,
            borderRadius: '4px',
            gap: '4px',
        },
        userSearchBannerIcon: {
            fontSize: '24px',
            marginBottom: '4px',
        },
        userSearchBannerText: {
            fontSize: '14px',
            fontWeight: 600,
            color: theme.palette.neutralPrimary,
            textAlign: 'center',
        },
        userSearchBannerNote: {
            fontSize: '13px',
            fontWeight: 400,
            color: theme.palette.neutralSecondary,
            textAlign: 'center',
        },
        configurationActionsRow: {
            display: 'flex',
            alignItems: 'center',
            marginTop: '8px',
            paddingTop: '8px',
        },
        configurationQueryAction: {
            display: 'flex',
            alignItems: 'center',
            flex: '1 1 auto',
        },
        reviewThresholdCallout: {
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'flex-end',
            gap: '8px',
            flex: '0 0 auto',
        },
        reviewThresholdCalloutText: {
            fontSize: '12px',
            color: theme.palette.neutralSecondary,
            whiteSpace: 'nowrap',
        },
    };
};
