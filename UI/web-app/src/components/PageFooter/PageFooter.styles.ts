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
        height: '100px',
      }, className],
      footer:{
        display: 'flex',
        justifyContent: 'space-between',
      }
    };
  };
  