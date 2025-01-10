// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type IStyle, type IStyleFunctionOrObject } from '@fluentui/react';
import type React from 'react';

export interface IEndpointsListStyles {
  root: IStyle;
  endpointsContainer: IStyle;
  itemTitle: IStyle;
  itemData: IStyle;
  outlookWarning: IStyle;
  outlookContainer: IStyle;
  yammerContainer: IStyle;
}

export interface IEndpointsListStyleProps {
  className?: string;
}

export interface IEndpointsListProps extends React.AllHTMLAttributes<HTMLDivElement> {
  /**
   * Optional className to apply to the root of the component.
   */
  className?: string;
  /**
   * Call to provide customized styling that will layer on top of the variant rules.
   */
  styles?: IStyleFunctionOrObject<IEndpointsListStyleProps, IEndpointsListStyles>;
  endpoints: string[];
  groupId: string;
  groupName?: string;
  showOutlookWarning?: boolean;
}
