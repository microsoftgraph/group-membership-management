// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice } from '@reduxjs/toolkit';
import type { RootState } from './store';
import { fetchOrgLeaderDetails } from './orgLeaderDetails.api';

type ObjectIdEmployeeIdMapping = Record<number, { objectId: string; text: string }>;

// Define a type for the slice state
export type orgLeaderDetails = {
  maxDepth: number;
  employeeId: number;
  objectId: string;
  text: string;
  partId: number;
  mapping: ObjectIdEmployeeIdMapping;
}

// Define the initial state using that type
const initialState: orgLeaderDetails = {
  maxDepth: 0,
  employeeId: -1,
  objectId: "",
  text: "",
  partId: 0,
  mapping: {},
};

export const orgLeaderDetailsSlice = createSlice({
  name: 'orgLeaderDetails',
  initialState,
  reducers: {
    updateOrgLeaderDetails: (state, action) => {
      state.employeeId = action.payload.employeeId;
    }
  },
  extraReducers: (builder) => {
    builder.addCase(fetchOrgLeaderDetails.fulfilled, (state, action) => {
      const updatedMapping = {
        ...state.mapping,
        [action.payload.employeeId]: { objectId: action.payload.objectId, text: action.payload.text }
      };
      return {
        maxDepth: action.payload.maxDepth,
        employeeId: action.payload.employeeId,
        partId: action.payload.partId,
        objectId: action.payload.objectId,
        text: action.payload.text,
        mapping: updatedMapping
      };
    });
  }
});

export const { updateOrgLeaderDetails } = orgLeaderDetailsSlice.actions;
export const selectOrgLeaderDetails = (state: RootState) => state.orgLeaderDetails;
export const selectObjectIdEmployeeIdMapping = (state: RootState) => state.orgLeaderDetails.mapping;
export default orgLeaderDetailsSlice.reducer;