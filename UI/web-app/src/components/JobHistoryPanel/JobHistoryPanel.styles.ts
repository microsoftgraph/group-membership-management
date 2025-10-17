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
        },
        header: {
            display: 'flex',
            justifyContent: 'flex-end',
        },

    };
};
