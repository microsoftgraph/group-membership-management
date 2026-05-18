// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { describe, expect, it } from 'vitest';
import profileReducer, {
  selectProfile,
  selectProfilePhoto,
  selectLastModifiedUserProfile,
  selectLastModifiedOnBehalfOfUserProfile,
} from './profile.slice';
import { getProfile, getProfilePhoto, getProfilePhotoUsingId } from './profile.api';

const initial = profileReducer(undefined, { type: '@@INIT' });

describe('profile.slice — extraReducers', () => {
  it('getProfile.fulfilled sets language', () => {
    const state = profileReducer(initial, getProfile.fulfilled('fr', 'req1', undefined as any));
    expect(state.userPreferredLanguage).toBe('fr');
  });

  it('getProfilePhoto.fulfilled sets photo', () => {
    const state = profileReducer(initial, getProfilePhoto.fulfilled('data:image/png;base64,...', 'req1', undefined as any));
    expect(state.userProfilePhoto).toBe('data:image/png;base64,...');
  });

  it('getProfilePhotoUsingId.fulfilled with lastModifiedBy sets userProfile', () => {
    const payload = { photoUrl: 'url1', displayName: 'Alice' };
    const state = profileReducer(
      initial,
      getProfilePhotoUsingId.fulfilled(payload, 'req1', { id: '123', type: 'lastModifiedBy' } as any)
    );
    expect(state.userProfile).toEqual({ photoUrl: 'url1', displayName: 'Alice' });
    expect(state.lastModifiedOnBehalfOfUserProfile).toBeUndefined();
  });

  it('getProfilePhotoUsingId.fulfilled with lastModifiedOnBehalfOf sets onBehalf profile', () => {
    const payload = { photoUrl: 'url2', displayName: 'Bob' };
    const state = profileReducer(
      initial,
      getProfilePhotoUsingId.fulfilled(payload, 'req1', { id: '456', type: 'lastModifiedOnBehalfOf' } as any)
    );
    expect(state.lastModifiedOnBehalfOfUserProfile).toEqual({ photoUrl: 'url2', displayName: 'Bob' });
    expect(state.userProfile).toBeUndefined();
  });

  it('getProfilePhotoUsingId.fulfilled with unknown type changes nothing', () => {
    const payload = { photoUrl: 'url3', displayName: 'C' };
    const state = profileReducer(
      initial,
      getProfilePhotoUsingId.fulfilled(payload, 'req1', { id: '789', type: 'other' } as any)
    );
    expect(state.userProfile).toBeUndefined();
    expect(state.lastModifiedOnBehalfOfUserProfile).toBeUndefined();
  });
});

describe('profile.slice — selectors', () => {
  const root = { profile: { ...initial, userProfilePhoto: 'pic', userProfile: { photoUrl: 'u', displayName: 'D' }, lastModifiedOnBehalfOfUserProfile: { photoUrl: 'u2', displayName: 'E' } } } as any;

  it('selectProfile returns profile state', () => {
    expect(selectProfile(root)).toBe(root.profile);
  });

  it('selectProfilePhoto returns photo', () => {
    expect(selectProfilePhoto(root)).toBe('pic');
  });

  it('selectLastModifiedUserProfile returns userProfile', () => {
    expect(selectLastModifiedUserProfile(root)).toEqual({ photoUrl: 'u', displayName: 'D' });
  });

  it('selectLastModifiedOnBehalfOfUserProfile returns onBehalf', () => {
    expect(selectLastModifiedOnBehalfOfUserProfile(root)).toEqual({ photoUrl: 'u2', displayName: 'E' });
  });
});
