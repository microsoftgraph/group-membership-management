// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { SourcePartQuery } from "./SourcePartQuery";

export type ISourcePart = {
    id: string;
    query: SourcePartQuery;
    isNew?: boolean;
};
