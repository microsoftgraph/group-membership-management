// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    type IPagingBarStyleProps,
    type IPagingBarStyles,
} from './PagingBar.types';

export const getStyles = (props: IPagingBarStyleProps): IPagingBarStyles => {
    const { className } = props;

    return {
        root: [{}, className],
        leftLabelMessage: {
            marginRight: 5
        },
        rightLabelMessage: {
            marginLeft: 5
        },
        divContainer: {
            display: "flex",
            alignItems: "center",
            marginLeft: 10,
            marginRight: 10
        },
        mainContainer: {
            display: "flex",
            justifyContent: "flex-end",
            marginRight: 36
        }
    };
};
