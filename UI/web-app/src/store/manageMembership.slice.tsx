// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice, type PayloadAction } from '@reduxjs/toolkit';
import { v4 as uuidv4 } from 'uuid';
import type { RootState } from './store';
import { NewJob } from '../models/NewJob';
import {
    getGroupOnboardingStatus,
    getChannelOnboardingStatus,
    getGroupEndpoints,
    getGroupOwners,
    searchDestinations,
    searchChannels
} from './manageMembership.api';
import { GroupOnboardingStatus, OnboardingStatus } from '../models/GroupOnboardingStatus';
import { Destination } from '../models/Destination';
import { DestinationPickerPersona, Job, GroupOwner } from '../models';
import { SyncJobQuery } from '../models/SyncJobQuery';
import { ISourcePart } from '../models/ISourcePart';
import { SourcePartType } from '../models/SourcePartType';
import { SourcePartQuery } from '../models/SourcePartQuery';
import { isSourcePartValid, removeUnusedProperties } from '../utils/sourcePartUtils';
import { createGroup } from './groups.api';
import { GroupSettings } from '../models/GroupSettings';
import { DestinationType } from '../models/DestinationType';

export interface ManageMembershipState {
    loadingSearchResults: boolean;
    searchResults?: DestinationPickerPersona[];
    channelPickerSearchResults?: DestinationPickerPersona[];
    selectedDestination: Destination | undefined;
    groupOwners?: GroupOwner[];
    onboardingStatus: GroupOnboardingStatus | null;
    hasChanges: boolean;
    currentStep: number;
    isAdvancedQueryValid: boolean;
    startDateOption: string;
    useThresholdLimits: string;
    showIncreaseDropdown: boolean;
    showDecreaseDropdown: boolean;
    newJob: NewJob;
    isAdvancedView: boolean;
    compositeQuery?: SyncJobQuery; // Made up of source parts' queries
    advancedViewQuery?: string;
    sourceParts: ISourcePart[];
    isEditingExistingJob: boolean;
    createdGroupId?: string | undefined;
    createdGroupName?: string | undefined;
    groupSettings?: GroupSettings | undefined;
    createGroupLoading: boolean;
    createGroupErrorMessage?: string | undefined;
    businessJustification?: string;
}

const initialState: ManageMembershipState = {
    loadingSearchResults: false,
    searchResults: [],
    channelPickerSearchResults: [],
    selectedDestination: undefined,
    groupOwners: [],
    onboardingStatus: null,
    hasChanges: false,
    currentStep: 0,
    isAdvancedQueryValid: false,
    startDateOption: 'ASAP',
    useThresholdLimits: 'Yes',
    showIncreaseDropdown: true,
    showDecreaseDropdown: true,
    newJob: {
        destination: '',
        requestor: '',
        lastModifiedOnBehalfOfDisplayName: '',
        lastModifiedOnBehalfOfObjectId: '',
        startDate: new Date().toISOString(),
        period: 24,
        query: {} as SyncJobQuery,
        titles: [],
        thresholdPercentageForAdditions: 100,
        thresholdPercentageForRemovals: 20,
        status: 'Idle',
        businessJustification: '',
    },
    isAdvancedView: false,
    compositeQuery: {} as SyncJobQuery,
    advancedViewQuery: '',
    sourceParts: [],
    isEditingExistingJob: false,
    createdGroupId: undefined,
    createdGroupName: undefined,
    groupSettings: undefined,
    createGroupLoading: false,
    createGroupErrorMessage: undefined,
    businessJustification: '',
};

const manageMembershipSlice = createSlice({
    name: 'manageMembership',
    initialState,
    reducers: {
        setHasChanges: (state, action: PayloadAction<boolean>) => {
            state.hasChanges = action.payload;
        },
        setCurrentStep: (state, action: PayloadAction<number>) => {
            state.currentStep = action.payload;
        },
        setSelectedDestination: (state, action: PayloadAction<Destination | undefined>) => {
            state.selectedDestination = action.payload;
            if (state.selectedDestination?.id === undefined) {
                state.onboardingStatus = null;
            }
        },
        setDestinationEndpoints: (state, action: PayloadAction<string[]>) => {
            if (state.selectedDestination) {
                state.selectedDestination = {
                    ...state.selectedDestination,
                    endpoints: action.payload,
                };
            }
        },
        setNewJobQuery: (state, action: PayloadAction<SyncJobQuery>) => {
            state.newJob.query = action.payload;
        },
        setNewJobRequestor: (state, action: PayloadAction<string>) => {
            state.newJob.requestor = action.payload;
        },
        setNewJobLastModifiedOnBehalfOfDisplayName: (state, action: PayloadAction<string>) => {
            state.newJob.lastModifiedOnBehalfOfDisplayName = action.payload;
        },
        setNewJobLastModifiedOnBehalfOfObjectId: (state, action: PayloadAction<string>) => {
            state.newJob.lastModifiedOnBehalfOfObjectId = action.payload;
        },
        setIsAdvancedQueryValid: (state, action: PayloadAction<boolean>) => {
            state.isAdvancedQueryValid = action.payload;
        },
        setNewJobStartDate: (state, action: PayloadAction<string>) => {
            state.newJob.startDate = action.payload;
        },
        setNewJobThresholdPercentageForAdditions: (state, action: PayloadAction<number>) => {
            state.newJob.thresholdPercentageForAdditions = action.payload;
        },
        setNewJobThresholdPercentageForRemovals: (state, action: PayloadAction<number>) => {
            state.newJob.thresholdPercentageForRemovals = action.payload;
        },
        setUseThresholdLimits: (state, action: PayloadAction<string>) => {
            state.useThresholdLimits = action.payload;
        },
        setStartDateOption: (state, action: PayloadAction<string>) => {
            state.startDateOption = action.payload;
        },
        setShowIncreaseDropdown: (state, action: PayloadAction<boolean>) => {
            state.showIncreaseDropdown = action.payload;
        },
        setShowDecreaseDropdown: (state, action: PayloadAction<boolean>) => {
            state.showDecreaseDropdown = action.payload;
        },
        resetManageMembership: () =>  initialState
        ,
        setIsAdvancedView: (state, action: PayloadAction<boolean>) => {
            if (action.payload) {
                // Switching to advanced view
                if(state.sourceParts.length === 0) {
                    state.advancedViewQuery = '';
                }
                else{
                    const compositeQuery = buildCompositeQuery(state.sourceParts);
                    const updatedSourceParts = state.sourceParts.map((part, index) => ({ 
                        ...part, 
                        query: compositeQuery[index],
                    }));
                    state.sourceParts = updatedSourceParts;
                    state.advancedViewQuery = JSON.stringify(compositeQuery);
                    state.isAdvancedQueryValid = true;
                    state.newJob.query = compositeQuery;
                }
            } else {
                // Switching from advanced view
                const advancedViewQuery = state.advancedViewQuery;
                const isAdvancedQueryValid = state.isAdvancedQueryValid;
                if (advancedViewQuery && advancedViewQuery !== '[]') {
                    try {
                        if(isAdvancedQueryValid){
                            const parsedQuery: SyncJobQuery = JSON.parse(advancedViewQuery);
                            state.compositeQuery = parsedQuery;
                            state.sourceParts = parsedQuery.map((query, index) => {
                                const originalPart = state.sourceParts[index];
                                return {
                                    id: state.sourceParts[index].id ?? uuidv4(),
                                    title: state.sourceParts[index].title ?? "",
                                    query: query,
                                    isNew: originalPart?.isNew ?? false,
                                    isExpanded: originalPart?.isExpanded ?? false
                                };
                            });
                            state.newJob.query = parsedQuery;
                        }
                    } catch (error) {
                        console.error(`Error parsing advanced view query:`, error);
                    }
                }
            }
            state.isAdvancedView = action.payload;
        },
        setAdvancedViewQuery: (state, action: PayloadAction<string>) => {
            if (!action.payload) return;
            state.advancedViewQuery = action.payload;
            const parsedQuery: SyncJobQuery = JSON.parse(action.payload);
            state.newJob.query = parsedQuery;
            state.sourceParts = parsedQuery.map((query, index) => {
                const originalPart = state.sourceParts[index];
                return {
                    id: state.sourceParts[index]?.id ?? uuidv4(),
                    title: state.sourceParts[index]?.title ?? "",
                    query: query,
                    isNew: originalPart?.isNew ?? false,
                    isExpanded: originalPart?.isExpanded ?? false
                };
            });
        },
        setCompositeQuery: (state, action: PayloadAction<SyncJobQuery | undefined>) => {
            state.compositeQuery = action.payload;
        },
        setSourceParts: (state, action: PayloadAction<ISourcePart[]>) => {
            state.sourceParts = action.payload;
        },
        addSourcePart: (state, action: PayloadAction<ISourcePart>) => {
            state.sourceParts.push(action.payload);
        },
        updateSourcePartType: (state, action: PayloadAction<{ partId: string; type: SourcePartType}>) => {
            const { partId, type } = action.payload;
            const partIndex = state.sourceParts.findIndex(part => part.id === partId);
            const currentQuery = state.sourceParts[partIndex].query;

            let updatedQuery: SourcePartQuery;
            switch (type) {
                case SourcePartType.HR:
                    updatedQuery = {
                        type: type,
                        source: {}
                    };
                    break;
                case SourcePartType.GroupMembership:
                    updatedQuery = {
                        type: type,
                        source: ""
                    };
                    break;
                case SourcePartType.GroupOwnership:
                    updatedQuery = {
                        type: type,
                        source: []
                    };
                    break;
                case SourcePartType.PlaceMembership:
                    updatedQuery = {
                        type: type,
                        source: ""
                    };
                    break;
                default:
                    updatedQuery = currentQuery;
            }
            state.sourceParts[partIndex] = {
                ...state.sourceParts[partIndex],
                query: updatedQuery
            };
            const compositeQuery = buildCompositeQuery(state.sourceParts);
            state.compositeQuery = compositeQuery;
            state.advancedViewQuery = JSON.stringify(compositeQuery);
            
        },
        updateSourcePart: (state, action: PayloadAction<ISourcePart>) => {
            const index = state.sourceParts.findIndex(part => part.id === action.payload.id);
            if (index !== -1) {
                if (state.sourceParts[index].title !== action.payload.title) {
                    state.sourceParts[index].title = action.payload.title;
                }
                state.sourceParts[index] = {
                    ...state.sourceParts[index],
                    ...action.payload,
                };
            }
        },

        copySourcePart: (state, action: PayloadAction<ISourcePart>) => {
            state.sourceParts.push(action.payload);
        },

        deleteSourcePart: (state, action: PayloadAction<string>) => {
            state.sourceParts = state.sourceParts.filter(part => part.id !== action.payload);
            const compositeQuery = buildCompositeQuery(state.sourceParts);
            state.compositeQuery = compositeQuery;
        },
        clearSourceParts: (state) => {
            state.sourceParts = [];
        },
        setJobDetailsForExistingJob: (state, action: PayloadAction<Job>) => {
            const { query, titles, requestor, lastModifiedOnBehalfOfDisplayName, lastModifiedOnBehalfOfObjectId, startDate, period, thresholdPercentageForAdditions, thresholdPercentageForRemovals, targetGroupId, targetGroupName, targetDestinationType, targetChannelId, targetChannelName } = action.payload;
            state.advancedViewQuery = JSON.stringify(query);
            state.compositeQuery = buildCompositeQuery(JSON.parse(query));
            state.sourceParts = JSON.parse(query).map((query: SourcePartQuery, index: number) => ({
                ...query,
                id: titles[index]?.partId || uuidv4(),
                query: query,
                isValid: true
            }));
            state.newJob.requestor = requestor || state.newJob.requestor;
            state.newJob.lastModifiedOnBehalfOfDisplayName = lastModifiedOnBehalfOfDisplayName || state.newJob.lastModifiedOnBehalfOfDisplayName;
            state.newJob.lastModifiedOnBehalfOfObjectId = lastModifiedOnBehalfOfObjectId || state.newJob.lastModifiedOnBehalfOfObjectId;
            state.newJob.startDate = startDate || state.newJob.startDate;
            state.newJob.period = period || state.newJob.period;
            state.newJob.thresholdPercentageForAdditions = thresholdPercentageForAdditions || state.newJob.thresholdPercentageForAdditions;
            state.newJob.thresholdPercentageForRemovals = thresholdPercentageForRemovals || state.newJob.thresholdPercentageForRemovals;
            
            // Set the selected destination for existing jobs to enable group owner fetching
            if (targetGroupId && targetGroupName) {
                state.selectedDestination = {
                    id: targetGroupId,
                    name: targetGroupName,
                    type: targetDestinationType || DestinationType.GroupMembership,
                    channelId: targetChannelId,
                    channelName: targetChannelName
                };
            }
        },
        setIsEditingExistingJob: (state, action: PayloadAction<boolean>) => {
            state.isEditingExistingJob = action.payload;
            if (!action.payload) {
                Object.assign(state, initialState);
            }
        },
        setCreatedGroupName: (state, action: PayloadAction<string | undefined>) => {
            state.createdGroupName = action.payload;
        },
        setCreateGroupErrorMessage: (state, action: PayloadAction<string>) => {
            state.createGroupErrorMessage = action.payload;
        },
        setBusinessJustification: (state, action: PayloadAction<string>) => {
            state.businessJustification = action.payload;
        },
        setGroupSettings: (state, action: PayloadAction<GroupSettings | undefined>) => {
            state.groupSettings = action.payload;
        }
    },
    extraReducers: (builder) => {
        builder.addCase(getGroupOnboardingStatus.fulfilled, (state, action) => {
            state.onboardingStatus = action.payload;
        });
        builder.addCase(getGroupOnboardingStatus.pending, (state, action) => {
            state.onboardingStatus = null;
        });
        builder.addCase(getChannelOnboardingStatus.fulfilled, (state, action) => {
            state.onboardingStatus = action.payload;
        });
        builder.addCase(getChannelOnboardingStatus.pending, (state, action) => {
            state.onboardingStatus = null;
        });
        builder.addCase(searchDestinations.fulfilled, (state, action) => {
            state.loadingSearchResults = false;
            state.searchResults = action.payload;
        });
        builder.addCase(searchDestinations.pending, (state) => {
            state.loadingSearchResults = true;
        });
        builder.addCase(searchDestinations.rejected, (state) => {
            state.loadingSearchResults = false;
        });
        builder.addCase(searchChannels.fulfilled, (state, action) => {
            state.loadingSearchResults = false;
            state.channelPickerSearchResults = action.payload;
        });
        builder.addCase(searchChannels.pending, (state) => {
            state.loadingSearchResults = true;
        });
        builder.addCase(searchChannels.rejected, (state) => {
            state.loadingSearchResults = false;
        });
        builder.addCase(getGroupEndpoints.fulfilled, (state, action) => {
            if (state.selectedDestination?.id === action.meta.arg) {
                state.selectedDestination.endpoints = action.payload;
            }
        });
        builder.addCase(createGroup.pending, (state) => {
            state.createGroupLoading = true;
        });
        builder.addCase(createGroup.fulfilled, (state, action) => {
            state.createGroupLoading = false;
            if (action.payload.groupId && state.createdGroupName) {
                state.createdGroupId = action.payload.groupId;
                state.selectedDestination = {
                    ...state.selectedDestination,
                    id: action.payload.groupId,
                    name: state.createdGroupName,
                    type: DestinationType.GroupMembership,
                };
                state.onboardingStatus = { status : OnboardingStatus.ReadyForOnboarding };
            }
            if (action.payload.responseData){
                state.createGroupErrorMessage = action.payload.responseData;
            }
        });
        builder.addCase(createGroup.rejected, (state, action) => {
            state.createGroupLoading = false;
            state.createGroupErrorMessage = action.error.message;
        });
        builder.addCase(getGroupOwners.fulfilled, (state, action) => {
            state.groupOwners = action.payload;
        });
    },
});

export const {
    setHasChanges,
    setCurrentStep,
    setNewJobQuery,
    setNewJobRequestor,
    setNewJobLastModifiedOnBehalfOfDisplayName,
    setNewJobLastModifiedOnBehalfOfObjectId,
    setIsAdvancedQueryValid,
    setSelectedDestination,
    setNewJobStartDate,
    setNewJobThresholdPercentageForAdditions,
    setNewJobThresholdPercentageForRemovals,
    setAdvancedViewQuery,
    setStartDateOption,
    setUseThresholdLimits,
    setShowIncreaseDropdown,
    setShowDecreaseDropdown,
    resetManageMembership,
    setIsAdvancedView,
    setCompositeQuery,
    setSourceParts,
    addSourcePart,
    updateSourcePartType,
    updateSourcePart,
    copySourcePart,
    deleteSourcePart,
    clearSourceParts,
    setJobDetailsForExistingJob,
    setIsEditingExistingJob,
    setCreatedGroupName,
    setCreateGroupErrorMessage,
    setBusinessJustification,
    setGroupSettings
} = manageMembershipSlice.actions;

// General
export const manageMembershipHasChanges = (state: RootState) => state.manageMembership.hasChanges;
export const manageMembershipCurrentStep = (state: RootState) => state.manageMembership.currentStep;
export const manageMembershipSelectedDestinationType = (state: RootState) => state.manageMembership.selectedDestination?.type;
export const manageMembershipSelectedDestinationName = (state: RootState) => state.manageMembership.selectedDestination?.name;
export const manageMembershipIsEditingExistingJob = (state: RootState) => state.manageMembership.isEditingExistingJob;

// Onboarding values
export const manageMembershipSelectedDestination = (state: RootState) => state.manageMembership.selectedDestination;
export const manageMembershipQuery = (state: RootState) => state.manageMembership.newJob.query;
export const manageMembershipStartDate = (state: RootState) => state.manageMembership.newJob.startDate;
export const manageMembershipPeriod = (state: RootState) => state.manageMembership.newJob.period;
export const manageMembershipThresholdPercentageForAdditions = (state: RootState) => state.manageMembership.newJob.thresholdPercentageForAdditions;
export const manageMembershipThresholdPercentageForRemovals = (state: RootState) => state.manageMembership.newJob.thresholdPercentageForRemovals;
export const manageMembershipRequestor = (state: RootState) => state.manageMembership.newJob.requestor;

// 1- Select Destination
export const manageMembershipSearchResults = (state: RootState) => state.manageMembership.searchResults;
export const manageMembershipChannelPickerSearchResults = (state: RootState) => state.manageMembership.channelPickerSearchResults;
export const manageMembershipLoadingSearchResults = (state: RootState) => state.manageMembership.loadingSearchResults;
export const manageMembershipSelectedDestinationEndpoints = (state: RootState) => state.manageMembership.selectedDestination?.endpoints;
export const manageMembershipGroupOnboardingStatus = (state: RootState) => state.manageMembership.onboardingStatus;
export const manageMembershipCreatedGroupId = (state: RootState) => state.manageMembership.createdGroupId;
export const manageMembershipCreatedGroupName = (state: RootState) => state.manageMembership.createdGroupName;
export const manageMembershipCreateGroupLoading = (state: RootState) => state.manageMembership.createGroupLoading;
export const manageMembershipCreateGroupErrorMessage = (state: RootState) => state.manageMembership.createGroupErrorMessage;
export const manageMembershipIsGroupReadyForOnboarding = (state: RootState): boolean => {
    return state.manageMembership.onboardingStatus?.status === OnboardingStatus.ReadyForOnboarding;
};
export const manageMembershipGroupSettings = (state: RootState) => state.manageMembership.groupSettings;

// 2- Membership Configuration
export const manageMembershipIsAdvancedView = (state: RootState) => state.manageMembership.isAdvancedView;
export const manageMembershipisAdvancedQueryValid = (state: RootState) => state.manageMembership.isAdvancedQueryValid;
export const manageMembershipCompositeQuery = (state: RootState) => state.manageMembership.compositeQuery;
export const manageMembershipAdvancedViewQuery = (state: RootState) => state.manageMembership.advancedViewQuery;
export const getSourcePartsFromState = (state: RootState) => state.manageMembership.sourceParts;
export const areAllSourcePartsValid = (state: RootState): boolean => {
    return state.manageMembership.sourceParts.every(isSourcePartValid);
};

// 4- Confirmation
export const manageMembershipBusinessJustification = (state: RootState) => state.manageMembership.businessJustification;
export const manageMembershipLastModifiedOnBehalfOfDisplayName = (state: RootState) => state.manageMembership.newJob.lastModifiedOnBehalfOfDisplayName;
export const manageMembershipLastModifiedOnBehalfOfObjectId = (state: RootState) => state.manageMembership.newJob.lastModifiedOnBehalfOfObjectId;
export const manageMembershipGroupOwners = (state: RootState) => state.manageMembership.groupOwners;

export const manageMembershipIsToggleEnabled = (state: RootState) => {
    const isAdvancedView = state.manageMembership.isAdvancedView;
    const isAdvancedViewQueryValid = state.manageMembership.isAdvancedQueryValid;
    const areAllSourcePartsValid = state.manageMembership.sourceParts.every(isSourcePartValid);
    if (isAdvancedView && isAdvancedViewQueryValid) {
        return true;
    }
    else if (!isAdvancedView && areAllSourcePartsValid) {
        return true;
    }
    else {
        return false;
    }
};

// 3- Run Configuration
export const manageMembershipStartDateOption = (state: RootState) => state.manageMembership.startDateOption;
export const manageMembershipUseThresholdLimits = (state: RootState) => state.manageMembership.useThresholdLimits;
export const manageMembershipShowIncreaseDropdown = (state: RootState) => state.manageMembership.showIncreaseDropdown;
export const manageMembershipShowDecreaseDropdown = (state: RootState) => state.manageMembership.showDecreaseDropdown;

export default manageMembershipSlice.reducer;

export function buildCompositeQuery(sourceParts: ISourcePart[]): SyncJobQuery {
    const compositeQuery: SyncJobQuery = sourceParts.map(part => part.query ? removeUnusedProperties(part.query) : part.query);
    return compositeQuery;
}