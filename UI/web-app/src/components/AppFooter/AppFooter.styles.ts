// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    type IAppFooterStyleProps,
    type IAppFooterStyles,
  } from './AppFooter.types';
  
  export const getStyles = (props: IAppFooterStyleProps): IAppFooterStyles => {
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
  