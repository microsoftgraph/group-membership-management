// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice, PayloadAction } from '@reduxjs/toolkit';
import { v4 as uuidv4 } from 'uuid';
import { RootState } from './store';
import { IChatMessage } from '../components/CopilotPanel/CopilotPanel.types';
import { ISourcePart } from '../models/ISourcePart';
import { sendCopilotMessage } from './copilot.api';

/**
 * Merges the complete resulting query returned by Copilot into the current source parts
 * **by `id`**: parts whose id matched an existing part keep that part's transient UI flags
 * (e.g. `isExpanded`), brand-new parts are appended, and parts absent from the resulting
 * query are dropped. The resulting query is authoritative, so this replaces the array
 * without unconditionally forcing `isNew: true` on every part.
 */
export function mergeResultingParts(existing: ISourcePart[], resulting: ISourcePart[]): ISourcePart[] {
    const existingById = new Map(existing.map(p => [p.id, p]));
    return resulting.map(part => {
        const prev = existingById.get(part.id);
        if (prev) {
            // Preserve transient UI state and existing "new" status for parts that already existed.
            return { ...part, isExpanded: prev.isExpanded, isNew: prev.isNew ?? false };
        }
        return part;
    });
}

export interface CopilotState {
    messages: IChatMessage[];
    conversationId: string;
    isLoading: boolean;
    error: string | null;
    isPanelOpen: boolean;
    lastSourceParts: ISourcePart[]; // Source parts from last chat response (each with its own org leader info)
    useOrgStructure: boolean; // Whether any part uses org hierarchy
    warning: string | null; // Soft warning from the last response (e.g. empty resulting query)
}

const initialState: CopilotState = {
    messages: [],
    conversationId: uuidv4(),
    isLoading: false,
    error: null,
    isPanelOpen: false,
    lastSourceParts: [],
    useOrgStructure: false,
    warning: null,
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
            state.conversationId = uuidv4();
            state.error = null;
            state.lastSourceParts = [];
            state.useOrgStructure = false;
            state.warning = null;
        },
        setError: (state, action: PayloadAction<string | null>) => {
            state.error = action.payload;
        },
        clearLastSourcePart: (state) => {
            state.lastSourceParts = [];
            state.useOrgStructure = false;
            state.warning = null;
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
                // Soft warning (e.g. the resulting query is now empty)
                state.warning = action.payload.warning ?? null;
            })
            .addCase(sendCopilotMessage.rejected, (state, action) => {
                state.isLoading = false;
                const payload = action.payload as { message?: string } | undefined;
                state.error = payload?.message || action.error.message || 'An error occurred';
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
export const selectCopilotWarning = (state: RootState) => state.copilot.warning;
export const selectIsPanelOpen = (state: RootState) => state.copilot.isPanelOpen;

export default copilotSlice.reducer;
