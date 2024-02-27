// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    type AdminConfigStyleProps,
    type AdminConfigStyles,
} from './AdminConfig.types';

export const getStyles = (props: AdminConfigStyleProps): AdminConfigStyles => {
    const { className, theme } = props;

    return {
        root: [{
            padding: '9px 36px 0px 36px'
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
        title: {
            fontWeight: 600,
            fontSize: 24,
            fontFamily: 'Segoe UI'
        },
        description: {
            padding: '10px 13px',
        },
        tiles: {
            display: 'flex',
            flexBasis: '100%',
            flexWrap: 'wrap',
            wrapFlow: 'row'
        },
        bottomContainer:{
            marginBottom: 24,
            display: 'flex',
            justifyContent: 'flex-end'
        },
        customLabelTextField: {
            borderRadius: 4,
            border: '1px solid',
            borderColor: theme.palette.neutralQuaternary,
            backgroud: theme.palette.white,
            width: 150,
        },
        defaultColumnSpan: {
            display: 'flex',
            alignItems: 'center',
            textAlign: 'center',
            height: 30,
            color: theme.palette.neutralPrimary
        },
        sourceNameTextFieldContainer: {
            marginTop: 17,
            padding: 4
        },
        sourceNameTextField: {
            borderRadius: 4,
            border: '1px solid',
            borderColor: theme.palette.neutralQuaternary,
            backgroud: theme.palette.white,
            width: 266,
        },
        sourceNameDescriptionContainer: {
            marginTop: 17
        },
        listOfAttributesTitleDescriptionContainer: {
            marginTop: 17,
            padding: '10px 0px 10px 0px'
        },
        detailsListContainer: {
            marginTop: 4,
            height: 700,
            overflowY: 'auto'
        },
        descriptionText: {
            fontWeight: 400,
            color: theme.palette.black
        }


    };
};


