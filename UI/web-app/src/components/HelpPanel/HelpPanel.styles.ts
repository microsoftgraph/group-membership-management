// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type IHelpPanelStyleProps, type IHelpPanelStyles } from './HelpPanel.types';

export const getStyles = (props: IHelpPanelStyleProps): IHelpPanelStyles => {
  const { className, theme } = props;

  return {
    root: [
      {
      },
      className,
    ],
    title:{
      fontSize: 16,
      fontWeight: 600,
      marginBottom: 10,
    },
    description:{
      fontSize: 14,
      fontWeight: 400,
      marginBottom: 10,
    }
  };
};
