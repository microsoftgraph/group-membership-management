// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice } from '@reduxjs/toolkit';
import { v4 as uuidv4 } from 'uuid';
import type { RootState } from './store';
import { fetchOrgLeaderDetails } from './orgLeaderDetails.api';

type ObjectIdEmployeeIdMapping = Record<number, { objectId: string; text: string; maxDepth: number; }>;

// Define a type for the slice state
export type orgLeaderDetails = {
  maxDepth: number;
  employeeId: number;
  objectId: string;
  text: string;
  partId: string;
  mapping: ObjectIdEmployeeIdMapping;
  orgLeaderDataReturned: boolean | undefined;
}

// Define the initial state using that type
const initialState: orgLeaderDetails = {
  maxDepth: 0,
  employeeId: -1,
  objectId: "",
  text: "",
  partId: uuidv4(),
  mapping: {},
  orgLeaderDataReturned: undefined,
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
        [action.payload.employeeId]: { objectId: action.payload.objectId, text: action.payload.text, maxDepth: action.payload.maxDepth },
      };
      return {
        maxDepth: action.payload.maxDepth,
        employeeId: action.payload.employeeId,
        partId: action.payload.partId,
        objectId: action.payload.objectId,
        text: action.payload.text,
        mapping: updatedMapping,
        orgLeaderDataReturned: true,
      };
    });
    builder.addCase(fetchOrgLeaderDetails.pending, (state) => {
      state.orgLeaderDataReturned = false;
    });
    builder.addCase(fetchOrgLeaderDetails.rejected, (state) => {
      state.orgLeaderDataReturned = false;
    });
  }
});

export const { updateOrgLeaderDetails } = orgLeaderDetailsSlice.actions;
export const selectOrgLeaderDetails = (state: RootState) => state.orgLeaderDetails;
export const selectObjectIdEmployeeIdMapping = (state: RootState) => state.orgLeaderDetails.mapping;
export const selectOrgLeaderDataReturned = (state: RootState) => state.orgLeaderDetails.orgLeaderDataReturned;
export default orgLeaderDetailsSlice.reducer;
