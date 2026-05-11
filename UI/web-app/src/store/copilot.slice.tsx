// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice, PayloadAction } from '@reduxjs/toolkit';
import { RootState } from './store';
import { IChatMessage } from '../components/CopilotPanel/CopilotPanel.types';
import { ISourcePart } from '../models/ISourcePart';
import { sendCopilotMessage } from './copilot.api';

export interface CopilotState {
    messages: IChatMessage[];
    isLoading: boolean;
    error: string | null;
    isPanelOpen: boolean;
    lastSourceParts: ISourcePart[]; // Source parts from last chat response (each with its own org leader info)
    useOrgStructure: boolean; // Whether any part uses org hierarchy
}

const initialState: CopilotState = {
    messages: [],
    isLoading: false,
    error: null,
    isPanelOpen: false,
    lastSourceParts: [],
    useOrgStructure: false,
};

const copilotSlice = createSlice({
    name: 'copilot',
    initialState,
    reducers: {
        addMessage: (state, action: PayloadAction<IChatMessage>) => {
            state.messages.push(action.payload);
            state.error = null;
        },
        clearMessages: (state) => {
            state.messages = [];
            state.error = null;
            state.lastSourceParts = [];
            state.useOrgStructure = false;
        },
        setError: (state, action: PayloadAction<string | null>) => {
            state.error = action.payload;
        },
        clearLastSourcePart: (state) => {
            state.lastSourceParts = [];
            state.useOrgStructure = false;
        },
        openPanel: (state) => {
            state.isPanelOpen = true;
        },
        closePanel: (state) => {
            state.isPanelOpen = false;
        },
    },
    extraReducers: (builder) => {
        builder
            .addCase(sendCopilotMessage.pending, (state) => {
                state.isLoading = true;
                state.error = null;
            })
            .addCase(sendCopilotMessage.fulfilled, (state, action) => {
                state.isLoading = false;
                state.messages.push(action.payload.message);
                // Store source parts from chat response (each with its own org leader info)
                state.lastSourceParts = action.payload.sourceParts;
                // Whether any part uses org hierarchy
                state.useOrgStructure = action.payload.useOrgStructure;
            })
            .addCase(sendCopilotMessage.rejected, (state, action) => {
                state.isLoading = false;
                state.error = action.error.message || 'An error occurred';
            });
    },
});

export const { addMessage, clearMessages, setError, clearLastSourcePart, openPanel, closePanel } = copilotSlice.actions;

// Selectors
export const selectCopilotMessages = (state: RootState) => state.copilot.messages;
export const selectCopilotIsLoading = (state: RootState) => state.copilot.isLoading;
export const selectCopilotError = (state: RootState) => state.copilot.error;
export const selectLastSourceParts = (state: RootState) => state.copilot.lastSourceParts;
export const selectUseOrgStructure = (state: RootState) => state.copilot.useOrgStructure;
export const selectIsPanelOpen = (state: RootState) => state.copilot.isPanelOpen;

export default copilotSlice.reducer;
