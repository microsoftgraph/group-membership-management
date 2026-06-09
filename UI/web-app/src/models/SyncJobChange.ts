// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export interface SyncJobChange {
    changeTime: string;
    changedByDisplayName: string | null;
    changedByObjectId: string | null;
    changedOnBehalfOfDisplayName: string | null;
    changedOnBehalfOfObjectId: string | null;
    changeReason: string | null;
    changeSource: string | null;
    changeDetails: string | null;
    businessJustification: string | null;
  };

