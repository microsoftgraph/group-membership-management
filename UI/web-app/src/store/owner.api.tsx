// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';

import { ThunkConfig } from './store';
import { TokenType } from '../services/auth';

export const addOwner = createAsyncThunk<string | undefined, string, ThunkConfig>(
  'owner/addOwner',
  async (groupId, {extra}) => {
    const { authenticationService } = extra.services;
    const token = await authenticationService.getTokenAsync(TokenType.Graph);
    const headers = new Headers();
    headers.append('Authorization', `Bearer ${token}`);
    headers.append('Scopes', 'Group.ReadWrite.All');
    headers.append('Content-Type', 'application/json');

    const options = {
      method: 'POST',
      headers,
      body: JSON.stringify({
        '@odata.id':
          'https://graph.microsoft.com/v1.0/serviceprincipals/913de83c-ec21-484d-aa74-84e364171851',
      }),
    };

    try {
      const url = `https://graph.microsoft.com/v1.0/groups/${groupId}/owners/$ref/`;
      const response = await fetch(url, options).then((response) => response);
      const payload: string =
        response.ok +
        ' ' +
        response.status.toString() +
        ' ' +
        response.statusText;
      return payload;
    } catch (error) {
      console.log(error);
    }
  }
);
