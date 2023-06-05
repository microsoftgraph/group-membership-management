// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit'

import { graphRequest } from '../authConfig'
import { msalInstance } from '../index'

export const addOwner = createAsyncThunk('owner/addOwner', async (groupId: string) => {
  const account = msalInstance.getActiveAccount()
  if (account == null) {
    throw Error(
      'No active account! Verify a user has been signed in and setActiveAccount has been called.'
    )
  }

  const authResult = await msalInstance.acquireTokenSilent({
    ...graphRequest,
    account
  })

  const headers = new Headers()
  const bearer = `Bearer ${authResult.accessToken}`
  headers.append('Authorization', bearer)
  headers.append('Scopes', 'Group.ReadWrite.All')
  headers.append('Content-Type', 'application/json')

  const options = {
    method: 'POST',
    headers,
    body: JSON.stringify({ '@odata.id': 'https://graph.microsoft.com/v1.0/serviceprincipals/913de83c-ec21-484d-aa74-84e364171851' })
  }

  try {
    const url = `https://graph.microsoft.com/v1.0/groups/${groupId}/owners/$ref/`
    const response = await fetch(url, options).then((response) => response)
    const payload: string = response.ok + ' ' + response.status.toString() + ' ' + response.statusText
    return payload
  } catch (error) {
    console.log(error)
  }
})
