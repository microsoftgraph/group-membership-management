// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { NewGroupType } from '../../models/NewGroupType';
import { PostGroupRequest } from '../../models/PostGroupRequest';

export const deriveGroupAlias = (name: string): string =>
  name.replace(/[^\x21-\x7e]|[@()\\[\]";:<>,]/g, '');

export const buildCreateGroupRequest = (draft: PostGroupRequest): PostGroupRequest => ({
  groupName: draft.groupName,
  groupAlias: draft.groupType === NewGroupType.Security ? deriveGroupAlias(draft.groupName) : draft.groupAlias,
  groupType: draft.groupType,
  ...(draft.description !== undefined ? { description: draft.description } : {}),
});
