// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import {
  DefaultButton,
  IProcessedStyleSet,
  Panel,
  PanelType,
  classNamesFunction,
  useTheme,
} from '@fluentui/react';
import { IHelpPanelProps, IHelpPanelStyleProps, IHelpPanelStyles } from './HelpPanel.types';
import { useStrings } from '../../localization/hooks';

const getClassNames = classNamesFunction<IHelpPanelStyleProps, IHelpPanelStyles>();

export const HelpPanelBase: React.FunctionComponent<IHelpPanelProps> = (props) => {
  const { className, styles, togglePanel, isPanelOpen } = props;
  const strings = useStrings();
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
        headerText={strings.needHelp}
        closeButtonAriaLabel={strings.close}
        onDismiss={togglePanel}
      >
        <div className={classNames.title}>{strings.HelpPanel.specificGuidanceTitle}</div>
        <div className={classNames.description}>{strings.HelpPanel.specificGuidanceDescription}</div>
        <DefaultButton text={strings.HelpPanel.openSite} onClick={onClick}></DefaultButton>
      </Panel>
  );
};
