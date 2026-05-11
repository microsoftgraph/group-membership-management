// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { describe, it, expect } from 'vitest';
import userProfileReducer, {
  clearProfile,
  selectUserProfile,
  selectUserProfileLoading,
  selectUserDepartment,
  selectUserCompany,
  selectUserManager,
  selectUserJobTitle,
  UserProfileState,
} from './userProfile.slice';
import { fetchMyProfile } from './userProfile.api';
import { UserProfile } from '../models/UserProfile';
import { RootState } from './store';

const initialState: UserProfileState = {
  profile: null,
  isLoading: false,
  error: null,
};

const mockProfile: UserProfile = {
  id: 'user-1',
  displayName: 'Pulkit Bhagat',
  department: 'Engineering',
  companyName: 'Contoso',
  jobTitle: 'Software Engineer',
  manager: {
    id: 'mgr-1',
    displayName: 'Jane Smith',
    mail: 'jsmith@contoso.com',
    mailNickname: 'jsmith',
  },
};

describe('userProfile.slice', () => {
  describe('reducers', () => {
    it('should return the initial state', () => {
      expect(userProfileReducer(undefined, { type: 'unknown' })).toEqual(initialState);
    });

    it('clearProfile should reset profile and error', () => {
      const populated: UserProfileState = {
        profile: mockProfile,
        isLoading: false,
        error: 'some error',
      };
      const state = userProfileReducer(populated, clearProfile());
      expect(state.profile).toBeNull();
      expect(state.error).toBeNull();
    });

    it('clearProfile should not affect isLoading', () => {
      const loading: UserProfileState = {
        profile: mockProfile,
        isLoading: true,
        error: null,
      };
      const state = userProfileReducer(loading, clearProfile());
      expect(state.isLoading).toBe(true);
    });
  });

  describe('extraReducers (fetchMyProfile)', () => {
    it('pending should set isLoading and clear error', () => {
      const stateWithError: UserProfileState = { ...initialState, error: 'old error' };
      const state = userProfileReducer(stateWithError, { type: fetchMyProfile.pending.type });
      expect(state.isLoading).toBe(true);
      expect(state.error).toBeNull();
    });

    it('fulfilled should set profile and stop loading', () => {
      const loadingState: UserProfileState = { ...initialState, isLoading: true };
      const state = userProfileReducer(loadingState, {
        type: fetchMyProfile.fulfilled.type,
        payload: mockProfile,
      });
      expect(state.isLoading).toBe(false);
      expect(state.profile).toEqual(mockProfile);
    });

    it('rejected should set error and stop loading', () => {
      const loadingState: UserProfileState = { ...initialState, isLoading: true };
      const state = userProfileReducer(loadingState, {
        type: fetchMyProfile.rejected.type,
        error: { message: 'Graph API failed' },
      });
      expect(state.isLoading).toBe(false);
      expect(state.error).toBe('Graph API failed');
    });

    it('rejected without message should use fallback error', () => {
      const state = userProfileReducer(initialState, {
        type: fetchMyProfile.rejected.type,
        error: {},
      });
      expect(state.error).toBe('Failed to fetch user profile');
    });
  });

  describe('selectors', () => {
    const mockRootState = {
      userProfile: {
        profile: mockProfile,
        isLoading: false,
        error: null,
      },
    } as unknown as RootState;

    it('selectUserProfile returns the profile', () => {
      expect(selectUserProfile(mockRootState)).toEqual(mockProfile);
    });

    it('selectUserProfileLoading returns loading state', () => {
      expect(selectUserProfileLoading(mockRootState)).toBe(false);
    });

    it('selectUserDepartment returns department', () => {
      expect(selectUserDepartment(mockRootState)).toBe('Engineering');
    });

    it('selectUserCompany returns company name', () => {
      expect(selectUserCompany(mockRootState)).toBe('Contoso');
    });

    it('selectUserManager returns manager object', () => {
      expect(selectUserManager(mockRootState)).toEqual(mockProfile.manager);
    });

    it('selectUserJobTitle returns job title', () => {
      expect(selectUserJobTitle(mockRootState)).toBe('Software Engineer');
    });

    it('selectors handle null profile gracefully', () => {
      const nullProfileState = {
        userProfile: { profile: null, isLoading: false, error: null },
      } as unknown as RootState;

      expect(selectUserProfile(nullProfileState)).toBeNull();
      expect(selectUserDepartment(nullProfileState)).toBeUndefined();
      expect(selectUserCompany(nullProfileState)).toBeUndefined();
      expect(selectUserManager(nullProfileState)).toBeUndefined();
      expect(selectUserJobTitle(nullProfileState)).toBeUndefined();
    });
  });
});
