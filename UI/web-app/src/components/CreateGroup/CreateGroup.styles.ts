// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type ICreateGroupStyleProps, type ICreateGroupStyles } from './CreateGroup.types';

export const getStyles = (props: ICreateGroupStyleProps): ICreateGroupStyles => {
  const { className, theme } = props;

  return {
    root: [{ display: 'flex', flexDirection: 'column', gap: 20, maxHeight: 'calc(100vh - 240px)', overflowY: 'auto', paddingRight: 4 }, className],
    alias: { display: 'flex', flexDirection: 'column', gap: 12 },
    label: { display: 'flex', alignItems: 'center', gap: 4 },
    help: { margin: '8px 0 0', fontSize: 12, lineHeight: 18, color: theme.semanticColors.bodySubtext },
    textField: {
      borderRadius: 4,
      border: '1px solid',
      borderColor: theme.palette.neutralQuaternary,
      background: theme.palette.white,
      width: '100%',
    },
  };
};
