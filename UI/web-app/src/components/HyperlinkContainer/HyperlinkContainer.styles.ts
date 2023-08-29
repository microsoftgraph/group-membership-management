// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type IHyperlinkContainerStyleProps, type IHyperlinkContainerStyles } from './HyperlinkContainer.types';

export const getStyles = (props: IHyperlinkContainerStyleProps): IHyperlinkContainerStyles => {
    const { className, theme } = props;

    return {
        root: [{
            padding: '0px 14px',
            display: 'flex',
            alignItems: 'flex-start',
            gap: '15px'
        }, className],
        card: {
            borderRadius: 10,
            marginBottom: 10,
            backgroundColor: theme.palette.white,
            margin: 10,
            outline: `1px solid ${theme.palette.neutralSecondaryAlt}`,
            display: 'flex',
            width: '649px',
            padding: 24,
            flexDirection: 'column',
            alignItems: 'flex-start',
            gap: '4px'
        },
        title: {
            fontWeight: 600,
            fontSize: 16
        },
        description:{
            fontSize: 14,
            fontWeight: 400
        }
    };
};