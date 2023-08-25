// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type IStackTokens, Stack, Text, TooltipHost } from '@fluentui/react';
import { Icon } from '@fluentui/react/lib/Icon';
import React from 'react';

export interface InfoLabelProps {
  label: string;
  description: string;
}

const stackTokens: IStackTokens = {
  childrenGap: 7
};

const titleStyle: React.CSSProperties = {
  fontSize: 14,
  fontWeight: 600
}

export const InfoLabel: React.FunctionComponent<InfoLabelProps> = (
  props: InfoLabelProps
) => {
  return (
    <Stack horizontal tokens={stackTokens}>
      <Text style={titleStyle}>{props.label}</Text>
      <TooltipHost content={props.description}>
        <Icon iconName="Info" />
      </TooltipHost>
    </Stack>
  );
};
