// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { classNamesFunction, type IProcessedStyleSet } from '@fluentui/react';
import { useTranslation } from 'react-i18next';
import { useTheme } from '@fluentui/react/lib/Theme';
import React from 'react';
import { TextField } from '@fluentui/react/lib/TextField';

import {
    type IHyperlinkContainerProps,
    type IHyperlinkContainerStyleProps,
    type IHyperlinkContainerStyles,
} from './HyperlinkContainer.types';

export const getClassNames = classNamesFunction<IHyperlinkContainerStyleProps, IHyperlinkContainerStyles>();

export const HyperlinkContainerBase: React.FunctionComponent<IHyperlinkContainerProps> = (
    props: IHyperlinkContainerProps
) => {
    const { title, description, className, styles } = props;
    const classNames: IProcessedStyleSet<IHyperlinkContainerStyles> = getClassNames(styles, {
        className,
        theme: useTheme(),
    });
    const { t } = useTranslation();


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
                    label={t('AdminConfig.hyperlinkContainer.address') as string}
                    placeholder={t('AdminConfig.hyperlinkContainer.addHyperlink') as string}
                ></TextField>
            </div>
        </div>
    )
};