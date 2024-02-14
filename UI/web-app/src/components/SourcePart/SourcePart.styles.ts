// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    type SourcePartStyleProps,
    type SourcePartStyles,
} from './SourcePart.types';

export const getStyles = (props: SourcePartStyleProps): SourcePartStyles => {
    const { className, theme } = props;

    return {
        root: [{
        }, className],
        card: {
            paddingTop: 18,
            paddingBottom: 18,
            paddingLeft: 22,
            paddingRight: 22,
            borderRadius: 10,
            marginBottom: 12,
            backgroundColor: theme.palette.white
        },
        header: {
            display: 'flex',
            flexDirection: 'row',
            justifyContent: 'space-between',
        },
        title: {
            fontWeight: 600,
            fontSize: 16,
            fontFamily: 'Segoe UI',
            marginRight: 'auto'
        },
        expandButton: {
            color: theme.semanticColors.bodyText,
            fontSize: 16,
            fontWeight: 600,
            fontFamily: 'Segoe UI'
        },
        content: {
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'stretch',
            height: '100%'
        },
        controls: {
            display: 'flex',
            flexDirection: 'row',
            flex: '0 1 auto',
            gap: 32
        },
        advancedQuery: {
            display: 'flex',
            flexDirection: 'row',
            alignItems: 'center',
            justifyContent: 'flex-start',
            flex: '1 0 auto',
            width: '100%',
        },
        exclusionaryPart: {
            display: 'flex',
            flexDirection: 'row',
            alignItems: 'center',
            flex: '0 1 auto',
            marginRight: '8px',
        },
        deleteButton: {
            marginLeft: 'auto',
            color: theme.semanticColors.primaryButtonBackground,
            borderColor: theme.semanticColors.primaryButtonBackground,
            borderRadius: 4
        },
        error: {
            color: theme.semanticColors.errorText,
            fontSize: 12,
            fontFamily: 'Segoe UI',
            fontWeight: 400,
            marginTop: 4,
            marginBottom: 4
        },
        dropdownTitle: {
            borderRadius: 4,
            borderStyle: 'solid',
            borderWidth: 1,
            borderColor: theme.palette.neutralQuaternary,
            background: theme.palette.white,
            minWidth: 200
        }
    };
};


