// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { SourcePartQuery } from './SourcePartQuery';

export type ISourcePart = {
    id: string;
    title: string;
    query: SourcePartQuery;
    isNew: boolean;
    isExpanded: boolean;
    /** Whether the AI detected that org hierarchy/manager context is needed. Used to auto-toggle includeOrg in HRQuerySource. */
    useOrgStructure?: boolean;
    /** Manager info to auto-select as org leader (when useOrgStructure is true). objectId is optional when AI extracts name from query. */
    managerToAutoSelect?: {
        objectId?: string;
        displayName: string;
        email?: string;
    };
    /** Depth to auto-select for org hierarchy (when useOrgStructure is true). undefined = all levels. */
    depthToAutoSelect?: number;
};
