// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type IStackTokens, Stack, Text, TooltipHost, format, useTheme } from '@fluentui/react';
import { Icon } from '@fluentui/react/lib/Icon';
import React from 'react';
import { useStrings } from '../store/hooks';

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
  const strings = useStrings();
  const theme = useTheme();

  return (
    <Stack horizontal tokens={stackTokens}>
      <Text style={titleStyle}>{props.label}</Text>
      <TooltipHost
        content={props.description}
        tabIndex={0}
        aria-label={format(strings.Components.InfoIcon.ariaLabel, props.label)}
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
    </Stack>
  );
};
