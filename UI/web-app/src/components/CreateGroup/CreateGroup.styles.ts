// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type ICreateGroupStyleProps, type ICreateGroupStyles } from './CreateGroup.types';

export const getStyles = (props: ICreateGroupStyleProps): ICreateGroupStyles => {
  const { className, theme } = props;

  return {
    root: [{}, className],
    textField: {
      borderRadius: 4,
      border: '1px solid',
      borderColor: theme.palette.neutralQuaternary,
      background: theme.palette.white,
      width: 500,
    },
  };
};
