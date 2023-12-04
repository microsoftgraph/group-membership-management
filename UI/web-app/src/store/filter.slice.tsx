// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { PayloadAction, createSlice } from '@reduxjs/toolkit';
import type { RootState } from './store';
import { getUsersForPeoplePicker } from './filter.api';
import { PeoplePickerPersona } from '../models/PeoplePickerPersona';

// Define a type for the slice state
export type FilterState = {
  peoplePickerSuggestions?: PeoplePickerPersona[];
}

// Define the initial state using that type
const initialState: FilterState = {
  peoplePickerSuggestions: [] ,
};

export const filterSlice = createSlice({
  name: 'filter',
  initialState,
  reducers: { },
  extraReducers: (builder) => {
    builder.addCase(getUsersForPeoplePicker.fulfilled, (state, {payload}: PayloadAction<PeoplePickerPersona[]>) => {
      state.peoplePickerSuggestions = payload;
    });
  }
});


export const selectPeoplePickerSuggestions = (state: RootState) => state.filter.peoplePickerSuggestions;
export default filterSlice.reducer;