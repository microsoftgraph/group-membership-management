// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { useTranslation } from 'react-i18next';
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
import { useDispatch, useSelector } from 'react-redux';
import { useEffect, useState } from 'react';
import { fetchSettingByKey, updateSetting } from '../../store/settings.api';
import { AppDispatch } from '../../store';

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
    const { t } = useTranslation();
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

    console.log("admin config, dashboardSetting: ", dashboardUrl);

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
        console.log('Clicked!');
        clearMessageBars();
        dispatch(updateSetting({ key: 'dashboardUrl', value: updatedDashboardUrl }))
            .then(() => {
                setSuccessMessage('Settings saved successfully');
            })
            .catch((error) => {
                setErrorMessage('Failed to save settings');
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
                            {t('AdminConfig.labels.pageTitle')}
                        </div>
                    </PageSection>
                </div>
                <div className={classNames.card}>
                    <PageSection>
                        <Pivot>
                            <PivotItem
                                headerText={t('AdminConfig.labels.hyperlinks') as string}
                                headerButtonProps={{
                                    'data-order': 1,
                                    'data-title': t('AdminConfig.labels.hyperlinks') as string,
                                }}
                            >
                                <div className={classNames.description}>
                                    {t('AdminConfig.labels.description')}
                                </div>
                                <div className={classNames.tiles}>
                                    <HyperlinkContainer
                                        title={t('AdminConfig.hyperlinkContainer.dashboardTitle')}
                                        description={t('AdminConfig.hyperlinkContainer.dashboardDescription')}
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