// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { Text, TooltipHost } from '@fluentui/react';
import { Icon } from '@fluentui/react/lib/Icon';
import React from 'react';

export interface InfoWordProps {
  label: string;
  description: JSX.Element | string;
}

export const InfoWord: React.FC<InfoWordProps> = ({ label, description }) => {
  return (
    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}>
      <Text variant="medium" style={{ fontWeight: 600 }}>{label}</Text>
      <TooltipHost content={description}>
        <Icon iconName="Info" />
      </TooltipHost>
    </span>
  );
};
