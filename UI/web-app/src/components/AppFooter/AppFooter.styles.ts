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
        flexDirection: 'column',
        justifyContent: 'center',
        width: '100%',
        height: '100%',
      },
      topRow: {
        display: 'flex',
        position: 'relative',
        alignItems: 'center',
        width: '100%',
      },
      pageVersion: {
        position: 'absolute',
        left: 0,
        top: '50%',
        transform: 'translateY(-50%)',
      },
      privacyPolicy: {
        padding: '10px 36px',
        width: '100%',
        textAlign: 'center',
        boxSizing: 'border-box',
      },
      pagingBar: {
        visibility: showPagingBar ? 'visible' : 'hidden'
      },
      rightControls: {
        display: 'flex',
        alignItems: 'center',
        width: '100%',
      },
      themeToggle: {
        padding: '20px 36px 20px 0',
      }
    };
  };

