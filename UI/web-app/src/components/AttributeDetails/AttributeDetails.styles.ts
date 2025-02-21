// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    type AttributeDetailsStyleProps,
    type AttributeDetailsStyles,
} from './AttributeDetails.types';

export const getStyles = (props: AttributeDetailsStyleProps): AttributeDetailsStyles => {
    const { className, theme } = props;

    return {
        root: [{
        }, className],
        close: {
            display: 'flex',
            justifyContent: 'flex-end'
        },
        modal:{
            padding: 20
        },
        content: {
            maxHeight: '400px',
            overflowY: 'auto',
            padding: '0 20px'
        },
        header: {
            display: 'flex',
            marginBottom: '10px'
        },
        attributeName: {
            width: '150px',
            fontWeight: 'bold'
        },
        attributeDescription: {
            marginLeft: '20px'
        },
        attributeNameHeader: {
            width: '150px',
            fontWeight: 'bold'
        },
        attributeDescriptionHeader: {
            marginLeft: '20px',
            fontWeight: 'bold'
        },
        attributeRow: {
            display: 'flex',
            marginBottom: '10px'
        },
        headerSeparator: {
            margin: '10px 0'
        },
        valueSeparator: {
            marginBottom: '25px'
        }
    };
};