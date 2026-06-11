// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    classNamesFunction,
    type IProcessedStyleSet,
    IconButton,
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
import { useSelector, useDispatch } from 'react-redux';
import { PagingBar } from '../PagingBar/PagingBar';
import { PrivacyPolicyLink } from '../PrivacyPolicyLink';
import { toggleTheme, selectIsDarkMode } from '../../store/theme.slice';
import { AppDispatch } from '../../store';
import { useStrings } from '../../store/hooks';

const getClassNames = classNamesFunction<
    IAppFooterStyleProps,
    IAppFooterStyles
>();

export const AppFooterBase: React.FunctionComponent<IAppFooterProps> = (
    props: IAppFooterProps
) => {
    const { className, styles } = props;
    const showPagingBar: boolean = useSelector(selectPagingBarVisible);
    const isDarkMode = useSelector(selectIsDarkMode);
    const strings = useStrings();
    const dispatch = useDispatch<AppDispatch>();
    const classNames: IProcessedStyleSet<IAppFooterStyles> = getClassNames(
        styles,
        {
            className,
            theme: useTheme(),
            showPagingBar
        }
    );

    const handleThemeToggle = () => {
        dispatch(toggleTheme());
    };

    return (
        <div className={classNames.footer}>
            <PageVersion />
            <PrivacyPolicyLink className={classNames.privacyPolicy} />
            <div className={classNames.rightControls}>
                {showPagingBar && (
                    <PagingBar />
                )}
                <div className={classNames.themeToggle}>
                    <IconButton
                        iconProps={{ iconName: isDarkMode ? 'Sunny' : 'ClearNight' }}
                        title={isDarkMode ? strings.Components.AppFooter.switchToLightMode : strings.Components.AppFooter.switchToDarkMode}
                        ariaLabel={isDarkMode ? strings.Components.AppFooter.switchToLightMode : strings.Components.AppFooter.switchToDarkMode}
                        onClick={handleThemeToggle}
                    />
                </div>
            </div>
        </div>
    );
};
