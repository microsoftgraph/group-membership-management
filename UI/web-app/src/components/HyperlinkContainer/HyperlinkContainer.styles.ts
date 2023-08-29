// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type IHyperlinkContainerStyleProps, type IHyperlinkContainerStyles } from './HyperlinkContainer.types';

export const getStyles = (props: IHyperlinkContainerStyleProps): IHyperlinkContainerStyles => {
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
            backgroundColor: theme.palette.white,
            width: 300,
            margin: 10,
            outline: `1px solid ${theme.palette.neutralLighterAlt}`
        },
        title: {
            fontWeight: 600,
            fontSize: 16,
            color: theme.palette.red
        },
        description:{

        }
    };
};