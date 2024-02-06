// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    type IAppFooterStyleProps,
    type IAppFooterStyles,
  } from './AppFooter.types';
  
  export const getStyles = (props: IAppFooterStyleProps): IAppFooterStyles => {
    const { className, showPagingBar } = props;
  
    return {
      root: [{
        height: '100px',
      }, className],
      footer: {
        display: 'flex',
        position: 'relative',
        alignItems: 'center',
        justifyContent: 'space-between',
        width: '100%',
      },
      privacyPolicy: {
        padding: '20px 36px',
        position: 'absolute',
        left: '50%',
        transform: 'translateX(-50%)',
      },
      pagingBar: {
        visibility: showPagingBar ? 'visible' : 'hidden'
      }
    };
  };
  