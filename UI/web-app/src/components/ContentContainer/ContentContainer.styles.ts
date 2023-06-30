// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type IContentContainerStyleProps, type IContentContainerStyles } from './ContentContainer.types';

export const getStyles = (props: IContentContainerStyleProps): IContentContainerStyles => {
    const { className, theme } = props;

    return {
        root: [{}, className],
        card: {
            padding: 10,
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
            flex: 1
        },
        linkButton: {
            // icon
            i: {
                paddingLeft: 5
            }
        }
    };
};