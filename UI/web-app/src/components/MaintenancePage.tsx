// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { Text } from '@fluentui/react/lib/Text';
import { Icon } from '@fluentui/react/lib/Icon';
import { useTheme } from '@fluentui/react';
import { useStrings } from '../store/hooks';

export const MaintenancePage = () => {
  const strings = useStrings();
  const theme = useTheme();
  return (
    <div style={{ padding: '20px', textAlign: 'center', display: 'flex', flexDirection: 'column', justifyContent: 'center', alignItems: 'center', minHeight: '100vh' }}>
      <Icon iconName="Settings" style={{ fontSize: '48px', color: theme.palette.neutralSecondary, marginBottom: '16px' }} />
      <Text variant="xxLarge" style={{ display: 'block', marginBottom: '8px' }}>
        {strings.maintenanceTitle}
      </Text>
      <Text variant="medium" style={{ color: theme.palette.neutralSecondary }}>
        {strings.maintenanceMessage}
      </Text>
    </div>
  );
};