// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type IContentContainerStyleProps, type IContentContainerStyles } from './ContentContainer.types';

export const getStyles = (props: IContentContainerStyleProps): IContentContainerStyles => {
    const { className, theme } = props;

    return {
        root: [{}, className],
        card: {
            paddingTop: 18,
            paddingBottom: 18,
            paddingLeft: 24,
            paddingRight: 24,
            borderRadius: 10,
            marginBottom: 10,
            backgroundColor: theme.palette.white
        },
        cardHeader: {
            display: "flex",
            alignItems: "baseline"
        },
        title: {
            textTransform: "uppercase",
            flex: 1,
            fontSize: '16px',
            fontStyle: 'normal',
            fontWeight: 600,
            lineHeight: '22px',
            color: theme.palette.neutralSecondary
        },
        linkButton: {
            // icon
            i: {
                paddingLeft: 5
            }
        }
    };
};