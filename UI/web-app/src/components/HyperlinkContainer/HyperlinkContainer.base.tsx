// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { classNamesFunction, type IProcessedStyleSet } from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import { TextField } from '@fluentui/react/lib/TextField';
import {
    type IHyperlinkContainerProps,
    type IHyperlinkContainerStyleProps,
    type IHyperlinkContainerStyles,
} from './HyperlinkContainer.types';
import { useStrings } from '../../localization/hooks';

export const getClassNames = classNamesFunction<IHyperlinkContainerStyleProps, IHyperlinkContainerStyles>();

export const HyperlinkContainerBase: React.FunctionComponent<IHyperlinkContainerProps> = (
    props: IHyperlinkContainerProps
) => {
    const { title, description, link, onUpdateLink, setHyperlinkError, className, styles } = props;
    const classNames: IProcessedStyleSet<IHyperlinkContainerStyles> = getClassNames(styles, {
        className,
        theme: useTheme(),
    });
    const strings = useStrings();

    const handleLinkChange = (newValue: string) => {
        onUpdateLink(newValue); 
    };

    const getErrorMessage = (value: string): string => {
        const isValid = isValidURL(value);
        setHyperlinkError(!isValid);
        return isValid ? '' : strings.AdminConfig.hyperlinkContainer.invalidUrl;
    };

    const isValidURL = (url: string): boolean => {
        try {
            new URL(url);
            return true;
        } catch (error) {
            return false;
        }
    }

    return (
        <div className={classNames.card}>
            <div className={classNames.title}>
                {title}
            </div>
            <div className={classNames.description}>
                {description}
            </div>
            <div>
                <TextField
                    label={strings.AdminConfig.hyperlinkContainer.address}
                    placeholder={strings.AdminConfig.hyperlinkContainer.addHyperlink}
                    value={link}
                    onChange={(e, newValue) => handleLinkChange(newValue ?? '')}
                    styles={{
                        fieldGroup: classNames.textFieldFieldGroup,
                    }}
                    onGetErrorMessage={getErrorMessage}
                    validateOnLoad={false}
                ></TextField>
            </div>
        </div>
    )
};