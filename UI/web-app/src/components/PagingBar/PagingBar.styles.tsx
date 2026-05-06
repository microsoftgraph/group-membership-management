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
            alignItems: "center",
            position: "relative",
            width: "100%",
            padding: "0 36px",
            boxSizing: "border-box"
        },
        navSection: {
            position: "absolute",
            left: "50%",
            transform: "translateX(-50%)",
            display: "flex",
            alignItems: "center"
        },
        displaySection: {
            marginLeft: "auto",
            display: "flex",
            alignItems: "center"
        }
    };
};
