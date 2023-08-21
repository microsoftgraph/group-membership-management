// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
  type IPageFooterStyleProps,
  type IPageFooterStyles,
} from './PageFooter.types';

export const getStyles = (props: IPageFooterStyleProps): IPageFooterStyles => {
  const { className, theme } = props;

  return {
    root: [{
      padding: '20px 0px 20px 24px',
      color: theme.palette.neutralSecondaryAlt
    }, className],
  };
};
