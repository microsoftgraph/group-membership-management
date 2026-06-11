// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { User } from '../models/User';
import { ApiBase } from './ApiBase';
import { IGraphApi } from './IGraphApi';
import { PeoplePickerPersona } from '../models/PeoplePickerPersona';
import { GraphResponseEntity, UserEntity } from './entities';
import { sortUsersByPrefix } from './utils/sort';
import {
  analyzeUserQuery,
  createUsersSearchRequest,
  createUsersFilterRequest,
  normalizeUsersResponse,
  mergeUsersById,
  mapUsersToPersonasWithPhotos,
  NormalizedUsersResponse,
} from './utils/users';


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
    // Entry: Convert freeform input into robust Graph /users queries and return
    // up to 10 persona suggestions, preferring precise prefix matches while
    // always keeping a fuzzy $search fallback for resilience.
    if (!input?.trim()) return [];

    // Detect alias/email-like input and nickname-like inputs for targeted filtering
    // Sanitizes for both $filter and $search contexts; see utils/users.sanitizeInput.
    const { trimmed, filterLiteral, searchSafe, isAliasLike, nameTokens, isAlphaWord, isNicknameLike } = analyzeUserQuery(input);
    const minPrefixLen = 3; // avoid aggressive filters for very short prefixes

    try {
      // Decide the query mode (see utils/users: analyzeUserQuery & createUsersFilterRequest for heuristics)
      const useNicknameFilter = (isNicknameLike && trimmed.length >= minPrefixLen);
      const useNameSingleTokenFilter = (!isAliasLike && isAlphaWord && nameTokens.length === 1 && trimmed.length >= minPrefixLen);

      // Always run $search for resiliency; optionally a precise $filter
      const searchReq = createUsersSearchRequest(this.httpClient, searchSafe);

      let filterReq: Promise<any> | undefined;
      // Targeted $filter based on heuristics for precise prefix matching.
      filterReq = createUsersFilterRequest(this.httpClient, {
        isAliasLike,
        isNicknameLike: useNicknameFilter,
        isNameSingleToken: useNameSingleTokenFilter,
        nameTokens,
        filterLiteral
      });

      // Normalize axios responses and safely extract values without any-casts
        const filterPromise: Promise<NormalizedUsersResponse> = filterReq
          ? normalizeUsersResponse(filterReq as Promise<{ data: { value: UserEntity[] } }>)
          : Promise.resolve({ data: { value: [] } });
        const searchPromise: Promise<NormalizedUsersResponse> = normalizeUsersResponse(searchReq as Promise<{ data: { value: UserEntity[] } }>);

        const results = await Promise.allSettled([filterPromise, searchPromise]);
      // Prefer precise filter results, then fuzzy search results
        const filterVal = results[0].status === 'fulfilled' ? results[0].value.data.value : [];
        const searchVal = results[1].status === 'fulfilled' ? results[1].value.data.value : [];

      // De-duplicate by id (filter-first priority) and cap to 10.
      const merged = mergeUsersById([filterVal, searchVal], 10);
      // Client-side ranking: prefix matches → word-boundary matches → others.
      const sorted = sortUsersByPrefix(merged, trimmed);
      // Enrich with small profile photos and map to PeoplePicker personas.
      return await mapUsersToPersonasWithPhotos(this.httpClient, sorted) as PeoplePickerPersona[];
    } catch (e) {
      // Fallback to $search if the chosen $filter isn't supported in this tenant
      const response = await createUsersSearchRequest(this.httpClient, searchSafe);
      const sorted = sortUsersByPrefix(response.data.value, trimmed);
      return await mapUsersToPersonasWithPhotos(this.httpClient, sorted) as PeoplePickerPersona[];
    }
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
