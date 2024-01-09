// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    classNamesFunction,
    type IProcessedStyleSet,
} from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import React from 'react';
import {
    type IAppFooterProps,
    type IAppFooterStyleProps,
    type IAppFooterStyles,
} from './AppFooter.types';
import { PageVersion } from '../PageVersion';
import { selectPagingBarVisible } from '../../store/pagingBar.slice';
import { useSelector } from 'react-redux';
import { PagingBar } from '../PagingBar/PagingBar';

const getClassNames = classNamesFunction<
    IAppFooterStyleProps,
    IAppFooterStyles
>();

export const AppFooterBase: React.FunctionComponent<IAppFooterProps> = (
    props: IAppFooterProps
) => {
    const { className, styles } = props;
    const classNames: IProcessedStyleSet<IAppFooterStyles> = getClassNames(
        styles,
        {
            className,
            theme: useTheme(),
        }
    );
    const showPagingBar: boolean = useSelector(selectPagingBarVisible);

    return (
        <div className={classNames.footer}>
            <PageVersion />
            {showPagingBar && (
                <PagingBar />
            )}
        </div>
    );
};
