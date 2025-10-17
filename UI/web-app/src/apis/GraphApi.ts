// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { User } from '../models/User';
import { ApiBase } from './ApiBase';
import { IGraphApi } from './IGraphApi';
import { PeoplePickerPersona } from '../models/PeoplePickerPersona';
import { GraphResponseEntity, UserEntity } from './entities';

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

  
  public async getPeoplePickerSuggestions(input: string): Promise<PeoplePickerPersona[]> {
    if (!input?.trim()) return [];
    const response = await this.httpClient.get<GraphResponseEntity<UserEntity[]>>(`/users`, {
      params: {
        $select: 'displayName,mail,id',
        $search: `"mail:${input}" OR "displayName:${input}" OR "userPrincipalName:${input}"`,
        $top: 10
      },
      headers: {
        'ConsistencyLevel': 'eventual',
      },
    });
    const users = response.data.value;

    const usersWithPhotos = await Promise.allSettled(users.map(async (user) => {
        try {
            const photoResponse = await this.httpClient.get(`/users/${user.id}/photo/$value`, { responseType: 'blob' });
            const photoUrl = URL.createObjectURL(photoResponse.data);
            return {
                ...user,
                photoUrl
            };
        } catch {
            return {
                ...user,
                photoUrl: null
            };
        }
    }));

    return usersWithPhotos.map((result, index) => {
      const user = result.status === 'fulfilled' ? result.value : { ...users[index], photoUrl: null };
      return {
        key: index,
        text: user.displayName,
        secondaryText: user.mail,
        id: user.id,
        imageUrl: user.photoUrl
      };
    });
  }

  public async getUser(objectId: string): Promise<string> {   
    const response = await this.httpClient.get<UserEntity>(`/users/${objectId}`, {});   
    return response.data.displayName;
  }

  public async getProfilePhotoUrlUsingUserId(userId: string): Promise<string> {
    try {
      const response = await this.httpClient.get<Blob>(`/users/${userId}/photos/48x48/$value`, { responseType: 'blob' });
      return URL.createObjectURL(response.data);
    } catch (error) {
      try {
        await this.getUser(userId);
        return 'ImageNotFound';
      } catch {
        return 'ErrorNonExistentStorage';
      }
    }
  }
};
