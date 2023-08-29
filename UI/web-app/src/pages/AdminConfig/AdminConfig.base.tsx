// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { useTranslation } from 'react-i18next';
import {
    classNamesFunction,
    IProcessedStyleSet,
    IStyleSet, Label, ILabelStyles, Pivot, PivotItem, PrimaryButton
} from '@fluentui/react';

import {
    IAdminConfigProps, IAdminConfigStyleProps, IAdminConfigStyles,
} from './AdminConfig.types';
import { PageSection } from '../../components/PageSection';
import { HyperlinkContainer } from '../../components/HyperlinkContainer';


const getClassNames = classNamesFunction<
    IAdminConfigStyleProps,
    IAdminConfigStyles
>();

const labelStyles: Partial<IStyleSet<ILabelStyles>> = {
    root: { marginTop: 10 },
};

export const AdminConfigBase: React.FunctionComponent<IAdminConfigProps> = (
    props: IAdminConfigProps
) => {
    const { className, styles } = props;

    const classNames: IProcessedStyleSet<IAdminConfigStyles> = getClassNames(
        styles,
        {
            className
        }
    );

    const { t } = useTranslation();

    // const {  } = props;

    return (
        <div className={classNames.root}>
            <PageSection>
                <div className={classNames.title}>
                    {t('AdminConfig.labels.pageTitle')}
                </div>
            </PageSection>
            <PageSection>
                <Pivot>
                    <PivotItem
                        headerText={t('AdminConfig.labels.hyperlinks') as string}
                        headerButtonProps={{
                            'data-order': 1,
                            'data-title': t('AdminConfig.labels.hyperlinks') as string,
                        }}
                    >
                        <div className={classNames.tiles}>
                            <HyperlinkContainer
                                title={t('AdminConfig.hyperlinkContainer.dashboardTitle')}
                                description={t('AdminConfig.hyperlinkContainer.dashboardDescription')}>
                            </HyperlinkContainer>
                            <HyperlinkContainer
                                title={t('AdminConfig.hyperlinkContainer.dashboardTitle')}
                                description={t('AdminConfig.hyperlinkContainer.dashboardDescription')}>
                            </HyperlinkContainer>
                            <HyperlinkContainer
                                title={t('AdminConfig.hyperlinkContainer.dashboardTitle')}
                                description={t('AdminConfig.hyperlinkContainer.dashboardDescription')}>
                            </HyperlinkContainer>
                            <HyperlinkContainer
                                title={t('AdminConfig.hyperlinkContainer.dashboardTitle')}
                                description={t('AdminConfig.hyperlinkContainer.dashboardDescription')}>
                            </HyperlinkContainer>
                        </div>
                    </PivotItem>
                </Pivot>
            </PageSection>
            <div className={classNames.bottomContainer}>
                <PrimaryButton text="Save"></PrimaryButton>
            </div>
        </div >
    )
}