// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { getGlobalClassNames } from '@fluentui/react'

import { type IAppStyleProps, type IAppStyles } from './App.types'

const GlobalClassNames = {
  root: 'gmm-app'
}

export const getStyles = (props: IAppStyleProps): IAppStyles => {
  const { className, theme } = props

  const classNames = getGlobalClassNames(GlobalClassNames, theme)

  return {
    root: [{
    }, classNames.root, className],
    body: {
      display: 'flex',
      flexDirection: 'row',
      flexGrow: 1,
      justifyContent: 'center',
      boxSizing: 'border-box',
      margin: '0 auto'
    },
    nav: {
      width: 324
    },
    content: {
      backgroundColor: theme.palette.white,
      paddingLeft: 40,
      paddingRight: 40,
      position: 'relative',
      width: '100%'
    }
  }
}
