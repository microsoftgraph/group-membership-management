// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type AccountInfo } from '@azure/msal-browser'
import { type IMsalContext } from '@azure/msal-react'
import { createAsyncThunk } from '@reduxjs/toolkit'

import { msalInstance } from '../index'
import { type Account } from '../models/Account'

export const fetchAccount = createAsyncThunk(
  'account/fetchAccount',
  async (context: IMsalContext) => {
    const account: AccountInfo | null = msalInstance.getActiveAccount()

    if (account == null) {
      try {
        context.instance.loginRedirect({
          scopes: ['User.Read']
        })
      } catch (error) {
        console.log(error)
      }

      const account = context.accounts[0]

      if (account) {
        const payload: Account = {
          ...account
        }

        return payload
      }
    } else {
      const payload: Account = {
        ...account
      }

      return payload
    }
  }
)
