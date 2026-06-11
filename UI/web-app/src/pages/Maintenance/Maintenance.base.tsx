// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import {
  IProcessedStyleSet,
  classNamesFunction,
  Text,
  Icon,
  useTheme,
} from '@fluentui/react';
import { IMaintenanceProps, IMaintenanceStyleProps, IMaintenanceStyles } from './Maintenance.types';
import { useStrings } from '../../store/hooks';

const getClassNames = classNamesFunction<
  IMaintenanceStyleProps,
  IMaintenanceStyles
>();

export const MaintenanceBase: React.FunctionComponent<IMaintenanceProps> = (
  props: IMaintenanceProps
) => {
  const { className, styles } = props;
  const strings = useStrings();
  const theme = useTheme();
  const classNames: IProcessedStyleSet<IMaintenanceStyles> = getClassNames(styles, {
    className,
    theme: theme,
  });

  return (
    <div className={classNames.root}>
      <div className={classNames.container}>
        <Icon iconName="Settings" className={classNames.icon} aria-hidden="true"/>
        <Text variant="xxLarge" className={classNames.title}>
          {strings.maintenanceTitle}
        </Text>
        <Text variant="medium" className={classNames.message}>
          {strings.maintenanceMessage}
        </Text>
      </div>
    </div>
  );
};
