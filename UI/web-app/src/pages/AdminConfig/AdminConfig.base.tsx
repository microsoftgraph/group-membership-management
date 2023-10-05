// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useState, useEffect } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import {
    classNamesFunction,
    IProcessedStyleSet,
    MessageBar, MessageBarType,
    Pivot, PivotItem, PrimaryButton
} from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import {
    IAdminConfigProps, IAdminConfigStyleProps, IAdminConfigStyles,
} from './AdminConfig.types';
import { PageSection } from '../../components/PageSection';
import { HyperlinkContainer } from '../../components/HyperlinkContainer';
import { Page } from '../../components/Page';
import { PageHeader } from '../../components/PageHeader';
import { selectSelectedSetting } from '../../store/settings.slice';
import { fetchSettingByKey, updateSetting } from '../../store/settings.api';
import { AppDispatch } from '../../store';
import { useStrings } from '../../localization/hooks';

const getClassNames = classNamesFunction<
    IAdminConfigStyleProps,
    IAdminConfigStyles
>();

export const AdminConfigBase: React.FunctionComponent<IAdminConfigProps> = (
    props: IAdminConfigProps
) => {
    const { className, styles } = props;

    const classNames: IProcessedStyleSet<IAdminConfigStyles> = getClassNames(
        styles,
        {
            className,
            theme: useTheme(),
        }
    );
    const strings = useStrings();
    const dispatch = useDispatch<AppDispatch>();
    const [updatedDashboardUrl, setUpdatedDashboardUrl] = useState('');
    const [successMessage, setSuccessMessage] = useState<string | undefined>(undefined);
    const [errorMessage, setErrorMessage] = useState<string | undefined>(undefined);
    const [hyperlinkError, setHyperlinkError] = useState(false);

    useEffect(() => {
        dispatch(fetchSettingByKey('dashboardUrl'));
    }, [dispatch]);

    useEffect(() => {
        clearMessageBars();
    }, [updatedDashboardUrl]);

    const dashboardUrl = useSelector(selectSelectedSetting);

    const clearMessageBars = () => {
        setSuccessMessage(undefined);
        setErrorMessage(undefined);
    };

    const handleLinkUpdate = (newValue: string) => {
        setUpdatedDashboardUrl(newValue);
        setSuccessMessage(undefined);
        setErrorMessage(undefined);
    };

    const onClick = () => {
        clearMessageBars();
        dispatch(updateSetting({ key: 'dashboardUrl', value: updatedDashboardUrl }))
            .then(() => {
                setSuccessMessage('Setting saved successfully');
            })
            .catch((error) => {
                setErrorMessage('Failed to save setting');
            });
    };

    return (
        <Page>
            {successMessage && (
                <MessageBar messageBarType={MessageBarType.success}>{successMessage}</MessageBar>
            )}
            {errorMessage && (
                <MessageBar messageBarType={MessageBarType.error}>{errorMessage}</MessageBar>
            )}
            <PageHeader />
            <div className={classNames.root}>
                <div className={classNames.card}>
                    <PageSection>
                        <div className={classNames.title}>
                            {strings.AdminConfig.labels.pageTitle}
                        </div>
                    </PageSection>
                </div>
                <div className={classNames.card}>
                    <PageSection>
                        <Pivot>
                            <PivotItem
                                headerText={strings.AdminConfig.labels.hyperlinks}
                                headerButtonProps={{
                                    'data-order': 1,
                                    'data-title': strings.AdminConfig.labels.hyperlinks,
                                }}
                            >
                                <div className={classNames.description}>
                                    {strings.AdminConfig.labels.description}
                                </div>
                                <div className={classNames.tiles}>
                                    <HyperlinkContainer
                                        title={strings.AdminConfig.hyperlinkContainer.dashboardTitle}
                                        description={strings.AdminConfig.hyperlinkContainer.dashboardDescription}
                                        link={updatedDashboardUrl || (dashboardUrl?.value ?? '')}
                                        onUpdateLink = {handleLinkUpdate}
                                        setHyperlinkError= {setHyperlinkError}>
                                    </HyperlinkContainer>
                                </div>
                            </PivotItem>
                        </Pivot>
                    </PageSection>
                </div>
                <div className={classNames.bottomContainer}>
                    <PrimaryButton text="Save" onClick={() => onClick() } disabled={hyperlinkError}></PrimaryButton>
                </div>
            </div >
        </Page>
    )
}