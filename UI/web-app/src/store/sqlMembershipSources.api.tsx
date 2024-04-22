// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';
import { ThunkConfig } from './store';
import { SqlMembershipAttribute, SqlMembershipAttributeValue } from '../models';
import { GetAttributeValuesResponse } from '../models/GetAttributeValuesResponse';
import { GetAttributeValuesRequest } from '../models/GetAttributeValuesRequest';

export const fetchDefaultSqlMembershipSource = createAsyncThunk<SqlMembershipAttribute, void, ThunkConfig>(
    'sqlMembershipSources/fetchDefaultSqlMembershipSource',
    async (_, { extra }) => {
      const { gmmApi } = extra.apis;
  
      try {
        return await gmmApi.sqlMembershipSources.fetchDefaultSqlMembershipSource();
      } catch (error) {
        throw new Error('Failed to fetch default SQL membership source!');
      }
    }
);

export const fetchDefaultSqlMembershipSourceAttributes = createAsyncThunk<SqlMembershipAttribute[], void, ThunkConfig>(
    'settings/fetchSqlFilterAttributes',
    async (_, { extra }) => {
      const { gmmApi } = extra.apis;
  
      try {
        return await gmmApi.sqlMembershipSources.fetchDefaultSqlMembershipSourceAttributes();
      } catch (error) {
        throw new Error('Failed to fetch default SQL filter attributes!');
      }
    }
  );

export const fetchAttributeValues = createAsyncThunk<GetAttributeValuesResponse, GetAttributeValuesRequest, ThunkConfig>(
  'fetchSqlFilterAttributeValues',
  async (request, { extra }) => {
    const { gmmApi } = extra.apis;
    try {
      const response = await gmmApi.sqlMembershipSources.fetchDefaultSqlMembershipSourceAttributeValues(request.attribute);
      const payload: GetAttributeValuesResponse = {
        values: response,
        attribute: request.attribute,
        type: request.type
      };
      return payload;
    } catch (error) {
      const payload: GetAttributeValuesResponse = {
        values: [],
        attribute: request.attribute,
        type: request.type
      };
      return payload;
    }
  }
);

export const patchDefaultSqlMembershipSourceCustomLabel = createAsyncThunk<void, string, ThunkConfig>(
    'sqlMembershipSources/patchDefaultSqlMembershipSourceCustomLabel',
    async (customLabel, { extra }) => {
      const { gmmApi } = extra.apis;
  
      try {
        return await gmmApi.sqlMembershipSources.patchDefaultSqlMembershipSourceCustomLabel(customLabel);
      } catch (error) {
        throw new Error('Failed to update default SQL membership source custom label!');
      }
    }
);

export const patchDefaultSqlMembershipSourceAttributes = createAsyncThunk<void, SqlMembershipAttribute[], ThunkConfig>(
    'sqlMembershipSources/patchDefaultSqlMembershipSourceAttributes',
    async (attributes, { extra }) => {
      const { gmmApi } = extra.apis;
  
      try {
        return await gmmApi.sqlMembershipSources.patchDefaultSqlMembershipSourceAttributes(attributes);
      } catch (error) {
        throw new Error('Failed to update default SQL membership source attributes!');
      }
    }
);