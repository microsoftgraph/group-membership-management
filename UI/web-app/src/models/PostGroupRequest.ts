// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { NewGroupType } from './NewGroupType';

export interface PostGroupRequest {
    groupName: string;
    groupAlias: string;
    groupType: NewGroupType;
};

