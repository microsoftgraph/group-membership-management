// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { useTranslation } from 'react-i18next';
import {
    classNamesFunction,
    IProcessedStyleSet,
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
import { fetchSettingByKey, postSetting } from '../../store/settings.api';
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

    useEffect(() => {
        dispatch(fetchSettingByKey('dashboardUrl'));
      }, [dispatch]);
      
    const dashboardUrl = useSelector(selectSelectedSetting);
    
    console.log("admin config, dashboardSetting: ", dashboardUrl);

    const [updatedDashboardUrl, setUpdatedDashboardUrl] = useState('');

    const onClick = () => {
        console.log('Clicked!');
        dispatch(postSetting({key: 'dashboardUrl', value: updatedDashboardUrl}));
    };

    return (
        <Page>
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
                                        link={updatedDashboardUrl || (dashboardUrl?.value ?? '')}>
                                    </HyperlinkContainer>
                                </div>
                            </PivotItem>
                        </Pivot>
                    </PageSection>
                </div>
                <div className={classNames.bottomContainer}>
                    <PrimaryButton text="Save" onClick={() => onClick()}></PrimaryButton>
                </div>
            </div >
        </Page>
    )
}