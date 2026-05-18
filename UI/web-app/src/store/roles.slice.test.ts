// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { describe, expect, it } from 'vitest';
import rolesReducer, {
  selectIsFetchingRoles,
  selectIsJobOwnerReader,
  selectIsJobOwnerEnabler,
  selectIsJobOwnerDeleter,
  selectIsJobTenantReader,
  selectIsJobTenantWriter,
  selectIsSubmissionReviewer,
  selectIsSubmissionRejector,
  selectIsHyperlinkAdministrator,
  selectIsCustomMembershipProviderAdministrator,
  selectIsOperationsResetAdministrator,
  selectIsGeneralSettingsAdministrator,
  selectHasAccess,
  selectHasJobWritePermissions,
  selectIsJobWriter,
  selectHasAdminCenterPermissions,
} from './roles.slice';
import { getAllRoles } from './roles.api';

const initial = rolesReducer(undefined, { type: '@@INIT' });

describe('roles.slice — extraReducers', () => {
  it('pending sets isFetchingRoles', () => {
    const state = rolesReducer(initial, getAllRoles.pending('r1', undefined as any));
    expect(state.isFetchingRoles).toBe(true);
  });

  it('fulfilled sets roles', () => {
    const payload = { isJobOwnerReader: true, isJobTenantWriter: true };
    const state = rolesReducer(initial, getAllRoles.fulfilled(payload as any, 'r1', undefined as any));
    expect(state.isFetchingRoles).toBe(false);
    expect(state.isJobOwnerReader).toBe(true);
    expect(state.isJobTenantWriter).toBe(true);
  });

  it('rejected clears isFetchingRoles', () => {
    const seeded = { ...initial, isFetchingRoles: true };
    const state = rolesReducer(seeded, getAllRoles.rejected(new Error('err'), 'r1', undefined as any));
    expect(state.isFetchingRoles).toBe(false);
  });
});

describe('roles.slice — selectors', () => {
  const makeRoot = (overrides = {}) => ({ roles: { ...initial, ...overrides } } as any);

  it('selectIsFetchingRoles', () => {
    expect(selectIsFetchingRoles(makeRoot({ isFetchingRoles: true }))).toBe(true);
  });

  it('selectIsJobOwnerReader', () => {
    expect(selectIsJobOwnerReader(makeRoot({ isJobOwnerReader: true }))).toBe(true);
  });

  it('selectIsJobOwnerEnabler', () => {
    expect(selectIsJobOwnerEnabler(makeRoot({ isJobOwnerEnabler: true }))).toBe(true);
  });

  it('selectIsJobOwnerDeleter', () => {
    expect(selectIsJobOwnerDeleter(makeRoot({ isJobOwnerDeleter: true }))).toBe(true);
  });

  it('selectIsJobTenantReader', () => {
    expect(selectIsJobTenantReader(makeRoot({ isJobTenantReader: true }))).toBe(true);
  });

  it('selectIsJobTenantWriter', () => {
    expect(selectIsJobTenantWriter(makeRoot({ isJobTenantWriter: true }))).toBe(true);
  });

  it('selectIsSubmissionReviewer', () => {
    expect(selectIsSubmissionReviewer(makeRoot({ isSubmissionReviewer: true }))).toBe(true);
  });

  it('selectIsSubmissionRejector', () => {
    expect(selectIsSubmissionRejector(makeRoot({ isSubmissionRejector: true }))).toBe(true);
  });

  it('selectIsHyperlinkAdministrator', () => {
    expect(selectIsHyperlinkAdministrator(makeRoot({ isHyperlinkAdministrator: true }))).toBe(true);
  });

  it('selectIsCustomMembershipProviderAdministrator', () => {
    expect(selectIsCustomMembershipProviderAdministrator(makeRoot({ isCustomMembershipProviderAdministrator: true }))).toBe(true);
  });

  it('selectIsOperationsResetAdministrator', () => {
    expect(selectIsOperationsResetAdministrator(makeRoot({ isOperationsResetAdministrator: true }))).toBe(true);
  });

  it('selectIsGeneralSettingsAdministrator', () => {
    expect(selectIsGeneralSettingsAdministrator(makeRoot({ isGeneralSettingsAdministrator: true }))).toBe(true);
  });

  describe('selectHasAccess', () => {
    it('returns false when no reader/writer roles', () => {
      expect(selectHasAccess(makeRoot())).toBe(false);
    });
    it('returns true for isJobOwnerReader', () => {
      expect(selectHasAccess(makeRoot({ isJobOwnerReader: true }))).toBe(true);
    });
    it('returns true for isJobOwnerWriter', () => {
      expect(selectHasAccess(makeRoot({ isJobOwnerWriter: true }))).toBe(true);
    });
    it('returns true for isJobTenantReader', () => {
      expect(selectHasAccess(makeRoot({ isJobTenantReader: true }))).toBe(true);
    });
    it('returns true for isJobTenantWriter', () => {
      expect(selectHasAccess(makeRoot({ isJobTenantWriter: true }))).toBe(true);
    });
  });

  describe('selectHasJobWritePermissions', () => {
    it('returns false with no write roles', () => {
      expect(selectHasJobWritePermissions(makeRoot())).toBe(false);
    });
    it('returns true for isJobOwnerWriter', () => {
      expect(selectHasJobWritePermissions(makeRoot({ isJobOwnerWriter: true }))).toBe(true);
    });
    it('returns true for isJobTenantWriter', () => {
      expect(selectHasJobWritePermissions(makeRoot({ isJobTenantWriter: true }))).toBe(true);
    });
  });

  describe('selectIsJobWriter', () => {
    it('returns false with no write roles', () => {
      expect(selectIsJobWriter(makeRoot())).toBe(false);
    });
    it('returns true for isJobOwnerWriter', () => {
      expect(selectIsJobWriter(makeRoot({ isJobOwnerWriter: true }))).toBe(true);
    });
  });

  describe('selectHasAdminCenterPermissions', () => {
    it('returns false with no admin roles', () => {
      expect(selectHasAdminCenterPermissions(makeRoot())).toBe(false);
    });
    it('returns true for isHyperlinkAdministrator', () => {
      expect(selectHasAdminCenterPermissions(makeRoot({ isHyperlinkAdministrator: true }))).toBe(true);
    });
    it('returns true for isCustomMembershipProviderAdministrator', () => {
      expect(selectHasAdminCenterPermissions(makeRoot({ isCustomMembershipProviderAdministrator: true }))).toBe(true);
    });
    it('returns true for isOperationsResetAdministrator', () => {
      expect(selectHasAdminCenterPermissions(makeRoot({ isOperationsResetAdministrator: true }))).toBe(true);
    });
    it('returns true for isGeneralSettingsAdministrator', () => {
      expect(selectHasAdminCenterPermissions(makeRoot({ isGeneralSettingsAdministrator: true }))).toBe(true);
    });
  });
});
