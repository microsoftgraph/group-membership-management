// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type IOwnerStyleProps, type IOwnerStyles } from './Owner.types'

export const getStyles = (props: IOwnerStyleProps): IOwnerStyles => {
  const { className } = props

  return {
    root: [
      {
        maxWidth: '20%'
      },
      className
    ]
  }
}
