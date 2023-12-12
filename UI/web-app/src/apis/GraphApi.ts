// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { User } from '../models/User';
import { ApiBase } from './ApiBase';
import { IGraphApi } from './IGraphApi';
import { PeoplePickerPersona } from '../models/PeoplePickerPersona';
import { GraphResponseEntity, PeoplePickerUserEntity } from './entities';

export class GraphApi extends ApiBase implements IGraphApi {
  public async getPreferredLanguage(user: User): Promise<string> {
    const response = await this.httpClient.get<string>(`/users/${user.id}`, {
      params: {
        $select: 'preferredLanguage',
      },
    });
    return response.data;
  }

  public async getProfilePhotoUrl(user: User): Promise<string> {
    const response = await this.httpClient.get<Blob>(`/users/${user.id}/photos/48x48/$value`, { responseType: 'blob' });
    return URL.createObjectURL(response.data);
  }

  
  public async getJobOwnerFilterSuggestions(displayName: string, mail: string): Promise<PeoplePickerPersona[]> {
    const response = await this.httpClient.get<GraphResponseEntity>(`/users`, {
      params: {
        $select: 'displayName,mail,id',
        $search: `"mail:${mail}" OR "displayName:${displayName}"`,
      },
      headers: {
        'ConsistencyLevel': 'eventual',
      },
    });
    var users = response.data.value as PeoplePickerUserEntity[];
    return users.map((user, index) => ({
      key: index,
      text: user.displayName,
      secondaryText: user.mail,
      id: user.id,
    }));

  }
}
