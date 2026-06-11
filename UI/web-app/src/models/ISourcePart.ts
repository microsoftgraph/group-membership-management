// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { SourcePartQuery } from './SourcePartQuery';

export type ISourcePart = {
    id: string;
    title: string;
    query: SourcePartQuery;
    isNew: boolean;
    isExpanded: boolean;
};
