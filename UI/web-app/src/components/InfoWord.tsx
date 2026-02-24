// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { Text, TooltipHost, format, useTheme } from '@fluentui/react';
import { Icon } from '@fluentui/react/lib/Icon';
import React from 'react';
import { useStrings } from '../store/hooks';

export interface InfoWordProps {
  label: string;
  description: JSX.Element | string;
}

export const InfoWord: React.FC<InfoWordProps> = ({ label, description }) => {
  const strings = useStrings();
  const theme = useTheme();

  return (
    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}>
      <Text variant="medium" style={{ fontWeight: 600 }}>{label}</Text>
      <TooltipHost
        content={description}
        tabIndex={0}
        aria-label={format(strings.Components.InfoIcon.ariaLabel, label)}
        styles={{
          root: {
            selectors: {
              ':focus-visible': {
                outline: `2px solid ${theme.semanticColors.focusBorder}`,
                outlineOffset: '2px',
                borderRadius: 4,
              },
            },
          },
        }}
      >
        <Icon iconName="Info" aria-hidden="true" />
      </TooltipHost>
    </span>
  );
};
