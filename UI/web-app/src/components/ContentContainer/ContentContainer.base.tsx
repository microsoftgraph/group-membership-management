// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { classNamesFunction, Icon, type IProcessedStyleSet } from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import React from 'react';
import { ActionButton } from '@fluentui/react/lib/Button';
import { Text } from '@fluentui/react/lib/Text';
import { Separator } from '@fluentui/react/lib/Separator';
import { Link } from '@fluentui/react/lib/Link';

import {
    type IContentContainerProps,
    type IContentContainerStyleProps,
    type IContentContainerStyles,
} from './ContentContainer.types';

export const getClassNames = classNamesFunction<IContentContainerStyleProps, IContentContainerStyles>();

export const ContentContainerBase: React.FunctionComponent<IContentContainerProps> = (
    props: IContentContainerProps
) => {
    const { title, actionOnClick, actionText, actionIcon, className, styles,
            children, useLinkButton, linkButtonIconName, hideSeparator, removeButton } = props;
    const classNames: IProcessedStyleSet<IContentContainerStyles> = getClassNames(styles, {
        className,
        theme: useTheme(),
    });

    return (

        <div className={classNames.card}>
            <div className={classNames.cardHeader}>
                <Text className={classNames.title}>
                    {title}
                </Text>
                {removeButton === true ? (<></>) :
                    useLinkButton === true ? (
                        <Link className={classNames.linkButton} onClick={actionOnClick}>
                            {actionText}<Icon iconName={linkButtonIconName} />
                        </Link>
                    ) : (
                        <ActionButton
                            iconProps={actionIcon}
                            allowDisabledFocus
                            onClick={actionOnClick}
                        >
                            {actionText}
                        </ActionButton>
                    )}
            </div>
            {hideSeparator === true ? <></> : <Separator />}
            {children}
        </div>

    )
};