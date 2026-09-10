// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';
import { v4 as uuidv4 } from 'uuid';
import { config } from '../authConfig';
import { ThunkConfig, RootState } from './store';
import { TokenType } from '../services/auth';
import { IChatMessage } from '../components/CopilotPanel/CopilotPanel.types';
import { ISourcePart } from '../models/ISourcePart';
import { HRSourcePart, HRSourcePartSource } from '../models/HRSourcePart';
import { GroupMembershipSourcePart } from '../models/GroupMembershipSourcePart';
import { SourcePartType } from '../models/SourcePartType';

// A single operation the server actually applied to the working query (empty list = nothing changed).
export interface CopilotOperationSummary {
    op: string;
    partId?: string;
}

export interface CopilotResponse {
    message: IChatMessage;
    sourceParts: ISourcePart[]; // The complete resulting query (each with its own org leader info)
    useOrgStructure: boolean; // Whether any part uses org hierarchy
    appliedOperations: CopilotOperationSummary[]; // Ops the server applied this turn; empty = no change
    warning?: string; // Soft warning (e.g., the resulting query is empty)
    errorCode?: string; // Set when the operation set was rejected (e.g., UnknownPartTarget)
}

// Backend API response format (v2: full resulting query after applying operations)
interface CopilotApiResponse {
    message: string;
    sourceParts?: ApiSourcePart[]; // v1 back-compat
    resultingQuery?: ApiSourcePart[]; // v2: complete authoritative query
    appliedOperations?: CopilotOperationSummary[];
    warning?: string;
    errorCode?: string;
}

// Backend source part format (now includes org leader info per-part and group membership support)
interface ApiSourcePart {
    partId: string;
    sourceType?: string; // "SqlMembership" (default) or "GroupMembership"
    filter: string | null; // Can be null for org-only queries or group membership
    title: string;
    isExclusion: boolean;
    useOrgStructure: boolean;
    orgLeaderName?: string;
    orgLeaderEmail?: string;
    orgLeaderObjectId?: string;
    orgLeaderDepth?: number;
    groupId?: string; // For GroupMembership: the Entra ID group's objectId
    groupName?: string; // For GroupMembership: the group's display name
}

/**
 * Builds the full working query the Copilot endpoint refines: every current source part
 * (including unsupported types Copilot cannot manipulate) is projected onto an ApiSourcePart
 * keyed by its stable `id` as `partId`, so the server can preserve unreferenced parts.
 */
export function buildWorkingQuery(sourceParts: ISourcePart[]): ApiSourcePart[] {
    return (sourceParts ?? []).map((part): ApiSourcePart => {
        const sourceType = part.query?.type ?? SourcePartType.HR;
        const isExclusion = !!part.query?.exclusionary;

        if (sourceType === SourcePartType.GroupMembership) {
            const source = (part.query as GroupMembershipSourcePart).source;
            return {
                partId: part.id,
                sourceType,
                filter: null,
                title: part.title ?? '',
                isExclusion,
                useOrgStructure: false,
                groupId: typeof source === 'string' ? source : undefined,
                groupName: part.title,
            };
        }

        if (sourceType === SourcePartType.HR) {
            const hrSource = (part.query as HRSourcePart).source as HRSourcePartSource;
            return {
                partId: part.id,
                sourceType,
                filter: hrSource?.filter ?? null,
                title: part.title ?? '',
                isExclusion,
                useOrgStructure: !!part.useOrgStructure || hrSource?.manager?.id != null,
                orgLeaderName: part.managerToAutoSelect?.displayName,
                orgLeaderEmail: part.managerToAutoSelect?.email,
                orgLeaderObjectId: part.managerToAutoSelect?.objectId,
                orgLeaderDepth: part.depthToAutoSelect ?? hrSource?.manager?.depth,
            };
        }

        // Unsupported source types (GroupOwnership, PlaceMembership, TeamsChannelMembership):
        // carry a minimal descriptor so the server preserves the part unchanged.
        return {
            partId: part.id,
            sourceType,
            filter: null,
            title: part.title ?? '',
            isExclusion,
            useOrgStructure: false,
        };
    });
}

function transformSourcePart(apiPart: ApiSourcePart, priorIds: Set<string>): ISourcePart {
    const isGroupMembership = apiPart.sourceType === 'GroupMembership' && !!apiPart.groupId;
    // A part is "new" only when the server minted an id we hadn't sent as working context.
    const isNew = !priorIds.has(apiPart.partId);

    if (isGroupMembership) {
        const groupQuery: GroupMembershipSourcePart = {
            type: SourcePartType.GroupMembership,
            source: apiPart.groupId!,
            exclusionary: apiPart.isExclusion,
        };

        return {
            id: apiPart.partId || uuidv4(),
            title: apiPart.title || apiPart.groupName || 'Group Source',
            query: groupQuery,
            isNew,
            isExpanded: false,
            createdViaAIQB: true,
        };
    }

    // Default: HR / SqlMembership source part
    const hrQuery: HRSourcePart = {
        type: SourcePartType.HR,
        source: {
            filter: apiPart.filter || undefined, // Convert null/empty to undefined
        },
        exclusionary: apiPart.isExclusion,
    };

    return {
        id: apiPart.partId || uuidv4(),
        title: apiPart.title,
        query: hrQuery,
        isNew,
        isExpanded: false,
        // Per-part org leader info
        useOrgStructure: apiPart.useOrgStructure || false,
        managerToAutoSelect: apiPart.orgLeaderName ? {
            objectId: apiPart.orgLeaderObjectId || undefined,
            displayName: apiPart.orgLeaderName,
            email: apiPart.orgLeaderEmail || undefined,
        } : undefined,
        depthToAutoSelect: apiPart.orgLeaderDepth ?? undefined,
        createdViaAIQB: true,
    };
}

export interface UserContext {
    managerName?: string;
    managerEmail?: string;
    managerAlias?: string;
}

export interface SendMessagePayload {
    message: string;
    userContext?: UserContext;
    hrAttributes?: { name: string; hasMapping: boolean; customLabel?: string; description?: string }[];
}

export const sendCopilotMessage = createAsyncThunk<CopilotResponse, SendMessagePayload, ThunkConfig>(
    'copilot/sendMessage',
    async ({ message: userMessage, userContext, hrAttributes }, { extra, getState, rejectWithValue }) => {
        const { authenticationService } = extra.services;
        const token = await authenticationService.getTokenAsync(TokenType.GMM);
        const headers = new Headers();
        const bearer = `Bearer ${token}`;
        headers.append('Authorization', bearer);
        headers.append('Content-Type', 'application/json');

        // Get conversation history and the current working query from state.
        // Exclude inline error entries (isError): they are UI-only failure markers and
        // must not be replayed to the model as assistant turns.
        const state = getState() as RootState;
        const conversationHistory = state.copilot.messages
            .filter(msg => !msg.isError)
            .map(msg => ({
                role: msg.role,
                content: msg.content,
            }));
        const currentSourceParts = state.manageMembership.sourceParts;
        const workingQuery = buildWorkingQuery(currentSourceParts);
        const priorIds = new Set(currentSourceParts.map(p => p.id));
        const existingById = new Map(currentSourceParts.map(p => [p.id, p]));

        const requestBody = {
            message: userMessage,
            conversationHistory,
            userContext,
            hrAttributes,
            workingQuery,
            conversationId: state.copilot.conversationId,
        };

        const options = {
            method: 'POST',
            headers,
            body: JSON.stringify(requestBody),
        };

        try {
            const response = await fetch(config.copilotChat, options);
            const responseText = await response.text();

            if (!response.ok) {
                return rejectWithValue({
                    message: 'Failed to send message to Copilot',
                    status: response.status,
                    statusText: response.statusText,
                    body: responseText,
                });
            }

            let data: CopilotApiResponse;
            try {
                data = JSON.parse(responseText) as CopilotApiResponse;
            } catch (parseError) {
                return rejectWithValue({
                    message: 'Failed to parse Copilot response',
                    body: responseText,
                    error: parseError instanceof Error ? parseError.message : String(parseError),
                });
            }

            const assistantMessage: IChatMessage = {
                id: uuidv4(),
                role: 'assistant',
                content: data.message,
                timestamp: new Date().toISOString(),
            };

            // v2 returns the complete resulting query; fall back to v1 sourceParts for compatibility.
            const apiParts = data.resultingQuery ?? data.sourceParts ?? [];

            // Apply by partId: supported parts are (re)built from the response; unsupported parts
            // Copilot cannot manipulate are reused unchanged from current state so nothing is lost.
            const sourceParts = apiParts.map(apiPart => {
                const isSupported = !apiPart.sourceType ||
                    apiPart.sourceType === SourcePartType.HR ||
                    apiPart.sourceType === SourcePartType.GroupMembership;
                const existing = existingById.get(apiPart.partId);
                if (!isSupported && existing) {
                    return existing;
                }
                return transformSourcePart(apiPart, priorIds);
            });

            // v2 (operation engine) always reports what it applied via appliedOperations, even as an
            // empty list on no-op turns. A legacy v1 response has no operation semantics (it only ever
            // returned the query via sourceParts), so preserve the original behavior for it: treat any
            // non-empty query as "changed" so Accept & Apply still shows. Detect v2 by the presence of
            // the appliedOperations field (v2 includes it, empty or not).
            const isV2Response = data.appliedOperations !== undefined;
            const appliedOperations: CopilotOperationSummary[] = isV2Response
                ? data.appliedOperations!
                : (sourceParts.length > 0 ? [{ op: 'set' }] : []);

            return {
                message: assistantMessage,
                sourceParts,
                useOrgStructure: sourceParts.some(p => p.useOrgStructure),
                // The server is authoritative about what changed. An empty list means the turn
                // was a no-op (describe/clarify/refuse) or the operation set was rejected, so the
                // UI should not offer "Accept & Apply" even though resultingQuery is non-empty.
                appliedOperations,
                warning: data.warning,
                errorCode: data.errorCode,
            };
        } catch (error) {
            return rejectWithValue({
                message: 'Failed to communicate with GMM Copilot. Please try again.',
                error: error instanceof Error ? error.message : String(error),
            });
        }
    }
);
