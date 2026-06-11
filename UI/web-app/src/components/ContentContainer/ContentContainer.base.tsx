// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { ActionButton, classNamesFunction, type IProcessedStyleSet } from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import React from 'react';
import { Text } from '@fluentui/react/lib/Text';
import { Separator } from '@fluentui/react/lib/Separator';

import {
  type IContentContainerProps,
  type IContentContainerStyleProps,
  type IContentContainerStyles,
} from './ContentContainer.types';

export const getClassNames = classNamesFunction<IContentContainerStyleProps, IContentContainerStyles>();

export const ContentContainerBase: React.FunctionComponent<IContentContainerProps> = (
  props: IContentContainerProps
) => {
  const { title, actionButtons, className, styles, children, hideSeparator } = props;
  const classNames: IProcessedStyleSet<IContentContainerStyles> = getClassNames(styles, {
    className,
    theme: useTheme(),
  });

  return (
    <div className={classNames.card}>
      <div className={classNames.cardHeader}>
        <Text className={classNames.title}>{title}</Text>
        {actionButtons?.map((button, index) => (
          <ActionButton
            iconProps={ button.icon }
            title={button.text}
            ariaLabel={button.text}
            onClick={button.onClick}>
            {button.text}
        </ActionButton>
        ))}
      </div>
      {hideSeparator === true ? <></> : <Separator />}
      {children}
    </div>
  );
};
