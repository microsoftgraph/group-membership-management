// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    type IStyle,
    type IStyleFunctionOrObject,
    type ITheme,
    type IProcessedStyleSet
} from '@fluentui/react';
import type React from 'react';

import { type Job } from '../../models/Job';

export interface IJobDetailsStyles {
    root: IStyle;
    itemTitle: IStyle;
    itemData: IStyle;
    card: IStyle;
    title: IStyle;
    subtitle: IStyle;
    toggleLabel: IStyle;
    jobEnabled: IStyle;
    jobDisabled: IStyle;
    membershipStatusContainer: IStyle;
    membershipStatusControls: IStyle;
    membershipStatusMessage: IStyle;
    requestor: IStyle;
    clockIcon: IStyle;
    membershipStatusActionButtons: IStyle;
    membershipStatusPendingLabel: IStyle;
    removeGMM: IStyle;
    historyButtonContainer: IStyle;
    userPersona: IStyle;
}

export interface IJobDetailsStyleProps {
    className?: string;
    theme: ITheme;
}

export interface IJobDetailsProps
    extends React.AllHTMLAttributes<HTMLDivElement> {
    /**
     * Optional className to apply to the root of the component.
     */
    className?: string;

    /**
     * Call to provide customized styling that will layer on top of the variant rules.
     */
    styles?: IStyleFunctionOrObject<IJobDetailsStyleProps, IJobDetailsStyles>;
}

export interface IContentProps extends React.AllHTMLAttributes<HTMLDivElement> {
    job: Job,
    classNames: IProcessedStyleSet<IJobDetailsStyles>
}

export interface IStatusContentProps extends React.AllHTMLAttributes<HTMLDivElement> {
    job: Job,
    resolveReview: () => void,
    classNames: IProcessedStyleSet<IJobDetailsStyles>
}