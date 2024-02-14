// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import type { GroupQuerySourceStyleProps, GroupQuerySourceStyles } from './GroupQuerySource.types';

export const getStyles = (props: GroupQuerySourceStyleProps): GroupQuerySourceStyles => {
  const { theme } = props;

  return {
    peoplePicker: {
      borderRadius: 4,
      border: '1px solid',
      borderColor: theme.palette.neutralQuaternary,
      backgroud: theme.palette.white,
      width: 500
    }
  };
};