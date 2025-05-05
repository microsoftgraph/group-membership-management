// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type IGroupSettingStyleProps, type IGroupSettingStyles } from './GroupSetting.types';

export const getStyles = (props: IGroupSettingStyleProps): IGroupSettingStyles => {
    const { className, theme } = props;

    return {
        root: [{}, className],
        textField: {
            borderRadius: 4,
            border: '1px solid',
            borderColor: theme.palette.neutralQuaternary,
            background: theme.palette.white,
            width: 500,
        },
        textFieldGroup: {
            border: 'none'
        },
        labelContainer: {
            display: 'flex',
            alignItems: 'center'
        },
        suggestionItems: {
            '.ms-Persona-secondaryText': {
                overflow: 'visible',
                textOverflow: 'clip'
            }
        },
        toggleContainer: {
            display: 'flex',
            flexDirection: 'row',
            paddingTop: 0
        }
    };
};
