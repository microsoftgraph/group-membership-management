// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice, type PayloadAction } from '@reduxjs/toolkit';

import { searchGroups } from './groups.api';
import type { RootState } from './store';
import { Group } from '../models/Group';

// Define a type for the slice state
export interface GroupsState {
    loading: boolean;
    searchResults?: Group[] | undefined;
}

// Define the initial state using that type
const initialState: GroupsState = {
    loading: false,
    searchResults: [],
};

export const groupsSlice = createSlice({
    name: 'groups',
    initialState,
    reducers: {
        setGroups: (state, action: PayloadAction<Group[]>) => {
            state.searchResults = action.payload;
        },
    },
    extraReducers: (builder) => {
        builder.addCase(searchGroups.fulfilled, (state, action) => {
            state.loading = false;
            state.searchResults = action.payload;
        });
        builder.addCase(searchGroups.pending, (state) => {
            state.loading = true;
        });
        builder.addCase(searchGroups.rejected, (state) => {
            state.loading = false;
        });
    },
});

export const { setGroups } = groupsSlice.actions;
export const selectGroups = (state: RootState) => state.groups;
export default groupsSlice.reducer;
