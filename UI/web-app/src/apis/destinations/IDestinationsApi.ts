// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { AxiosResponse } from 'axios';
import { PostGroupRequest } from '../../models/PostGroupRequest';
import { PostGroupResponse } from '../../models/PostGroupResponse';

export interface IDestinationsApi {
  createGroup(groupName: PostGroupRequest): Promise<AxiosResponse<PostGroupResponse>>;
};
