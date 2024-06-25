// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    type INotFoundStyleProps,
    type INotFoundStyles,
} from './NotFound.types';

export const getStyles = (props: INotFoundStyleProps): INotFoundStyles => {
    const { className } = props;

    return {
        root: [{
            padding: '9px 36px 0px 36px',
            display: 'flex',
            justifyContent: 'center',
            alignItems: 'center',
            height: '20vh',
        }, className],
        container: {
            maxWidth: '600px',
            alignItems: 'center',
            textAlign: 'center',
            fontSize: '20px',
        }
    };
};


