// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice, PayloadAction } from '@reduxjs/toolkit';
import { v4 as uuidv4 } from 'uuid';
import { RootState } from './store';
import { IChatMessage } from '../components/CopilotPanel/CopilotPanel.types';
import { ISourcePart } from '../models/ISourcePart';
import { HRSourcePart } from '../models/HRSourcePart';
import { SourcePartType } from '../models/SourcePartType';
import { sendCopilotMessage, CopilotOperationSummary } from './copilot.api';

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
        if (!prev) {
            return part;
        }
        // Preserve transient UI state and existing "new" status for parts that already existed.
        const merged: ISourcePart = { ...part, isExpanded: prev.isExpanded, isNew: prev.isNew ?? false };

        // Carry forward a previously-resolved org leader (query.source.manager) when a matched HR part
        // comes back without one. Copilot never edits the HR manager id directly — it is resolved
        // client-side — so a matched HR part that still intends org structure but has no manager.id
        // means the leader was unchanged. Without this, a filter-only refine (or an existing DB-loaded
        // org rule) would silently lose its resolved org leader on apply.
        //
        // Guards against reintroducing a leader the refine intentionally changed or removed:
        //   - merged.useOrgStructure must still be true (a refine that turned org structure off keeps
        //     manager dropped);
        //   - no new leader may be named this turn (managerToAutoSelect absent) — a named leader,
        //     whether or not it resolved to an objectId, must not be overwritten by the stale one.
        const namedNewLeader = merged.managerToAutoSelect != null;
        if (
            merged.query?.type === SourcePartType.HR &&
            prev.query?.type === SourcePartType.HR &&
            merged.useOrgStructure === true &&
            !namedNewLeader
        ) {
            const newSource = (merged.query as HRSourcePart).source;
            const prevSource = (prev.query as HRSourcePart).source;
            if (newSource?.manager?.id == null && prevSource?.manager?.id != null) {
                merged.query = {
                    ...(merged.query as HRSourcePart),
                    source: {
                        ...newSource,
                        manager: { ...prevSource.manager },
                    },
                };
            }
        }
        return merged;
    });
}

export interface CopilotState {
    messages: IChatMessage[];
    conversationId: string;
    isLoading: boolean;
    error: string | null;
    isPanelOpen: boolean;
    lastSourceParts: ISourcePart[]; // Source parts from last chat response (each with its own org leader info)
    lastAppliedOperations: CopilotOperationSummary[]; // Ops the server applied last turn; empty = no change (hides Accept & Apply)
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
    lastAppliedOperations: [],
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
            state.lastAppliedOperations = [];
            state.useOrgStructure = false;
            state.warning = null;
        },
        setError: (state, action: PayloadAction<string | null>) => {
            state.error = action.payload;
        },
        clearLastSourcePart: (state) => {
            state.lastSourceParts = [];
            state.lastAppliedOperations = [];
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
                // Ops the server actually applied this turn. Empty means nothing changed
                // (describe/clarify/refuse or a rejected op set), which gates Accept & Apply.
                state.lastAppliedOperations = action.payload.appliedOperations ?? [];
                // Whether any part uses org hierarchy
                state.useOrgStructure = action.payload.useOrgStructure;
                // Soft warning (e.g. the resulting query is now empty)
                state.warning = action.payload.warning ?? null;
            })
            .addCase(sendCopilotMessage.rejected, (state, action) => {
                state.isLoading = false;
                const payload = action.payload as { message?: string } | undefined;
                const errorText = payload?.message || action.error.message || 'An error occurred';
                state.error = errorText;
                // Persist the failure as an inline transcript entry so it stays visible in the
                // chat window and is captured by "Copy conversation" (e.g. a "please try again"
                // failure). Flagged with isError so it renders with error styling and is excluded
                // from the conversationHistory sent back to the model on the next turn.
                state.messages.push({
                    id: uuidv4(),
                    role: 'assistant',
                    content: errorText,
                    timestamp: new Date().toISOString(),
                    isError: true,
                });
            });
    },
});

export const { addMessage, clearMessages, setError, clearLastSourcePart, openPanel, closePanel } = copilotSlice.actions;

// Selectors
export const selectCopilotMessages = (state: RootState) => state.copilot.messages;
export const selectCopilotConversationId = (state: RootState) => state.copilot.conversationId;
export const selectCopilotIsLoading = (state: RootState) => state.copilot.isLoading;
export const selectCopilotError = (state: RootState) => state.copilot.error;
export const selectLastSourceParts = (state: RootState) => state.copilot.lastSourceParts;
export const selectLastAppliedOperations = (state: RootState) => state.copilot.lastAppliedOperations;
export const selectUseOrgStructure = (state: RootState) => state.copilot.useOrgStructure;
export const selectCopilotWarning = (state: RootState) => state.copilot.warning;
export const selectIsPanelOpen = (state: RootState) => state.copilot.isPanelOpen;

export default copilotSlice.reducer;
