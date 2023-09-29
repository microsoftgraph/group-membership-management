// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  DefaultButton,
  IProcessedStyleSet,
  Panel,
  PanelType,
  classNamesFunction,
  useTheme,
} from '@fluentui/react';
import { IHelpPanelProps, IHelpPanelStyleProps, IHelpPanelStyles } from './HelpPanel.types';

const getClassNames = classNamesFunction<IHelpPanelStyleProps, IHelpPanelStyles>();

export const HelpPanelBase: React.FunctionComponent<IHelpPanelProps> = (props) => {
  const { className, styles, togglePanel, isPanelOpen } = props;
  const { t } = useTranslation();
  const theme = useTheme();

  const classNames: IProcessedStyleSet<IHelpPanelStyles> = getClassNames(styles, {
    className,
    theme
  });

  const onClick = () => {
  };

  return (
      <Panel
        isOpen={isPanelOpen}
        type={PanelType.medium}
        headerText={t('needHelp') as string}
        closeButtonAriaLabel={t('close') as string}
        onDismiss={togglePanel}
      >
        <div className={classNames.title}>{t('HelpPanel.specificGuidanceTitle')}</div>
        <div className={classNames.description}>{t('HelpPanel.specificGuidanceDescription')}</div>
        <DefaultButton text={t('HelpPanel.openSite') as string} onClick={onClick}></DefaultButton>
      </Panel>
  );
};
