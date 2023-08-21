// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    type IStyle,
    type IStyleFunctionOrObject
} from '@fluentui/react';
import type React from 'react';

export interface IPagingBarStyles {
    root: IStyle;
    leftLabelMessage: IStyle;
    rightLabelMessage: IStyle;
    divContainer: IStyle;
    mainContainer: IStyle;
}

export interface IPagingBarStyleProps {
    className?: string;
}

export interface IPagingBarProps extends React.AllHTMLAttributes<HTMLElement> {
    /**
     * Optional className to apply to the root of the component.
     */
    className?: string;
    /**
     * Call to provide customized styling that will layer on top of the variant rules.
     */
    styles?: IStyleFunctionOrObject<IPagingBarStyleProps, IPagingBarStyles>;
    pageSize: string;
    pageNumber: number;
    totalNumberOfPages: number;
    setPageSize: (pageSize: string) => void;
    setPageNumber: (pageNumber: number) => void;
    setPageSizeCookie: (pageSize: string) => void;
    getJobsByPage: (currentPageSize?: number, currentPageNumber?: number) => void;
}