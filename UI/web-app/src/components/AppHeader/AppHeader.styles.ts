// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { IAppHeaderStyleProps, IAppHeaderStyles } from './AppHeader.types';

export const getStyles = (props: IAppHeaderStyleProps): IAppHeaderStyles => {
  const { className, theme } = props;

  return {
    root: [
      {
        backgroundColor: theme.palette.themePrimary,
        height: 54,
        maxWidth: "100%",
        margin: '0 auto',
        borderBottom: '1px solid transparent',
        boxSizing: 'border-box',
        display: 'flex'
      },
      className
    ]
  };
};
