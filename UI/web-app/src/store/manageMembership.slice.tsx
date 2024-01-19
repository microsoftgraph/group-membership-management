// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice, type PayloadAction } from '@reduxjs/toolkit';

import type { RootState } from './store';
import { NewJob } from '../models/NewJob';
import {
    getGroupOnboardingStatus,
    getGroupEndpoints,
    searchDestinations
} from './manageMembership.api';
import { OnboardingStatus } from '../models/GroupOnboardingStatus';
import { Destination } from '../models/Destination';
import { DestinationPickerPersona } from '../models';
import { HRSourcePart } from '../models/HRSourcePart';
import { SyncJobQuery } from '../models/SyncJobQuery';

export interface ISourcePart {
    id: number;
    query: HRSourcePart;
    isValid: boolean;
    isExclusionary?: boolean;
}

export interface Query {
    sourceParts: ISourcePart[];
}

export const placeholderQueryHRPart: HRSourcePart = {
  type: "SqlMembership",
  source: {
    ids: [],
    filter: "",
    depth: 1
  },
  exclusionary: false
};


export const placeholderAdvancedViewQuery: SyncJobQuery = [
    placeholderQueryHRPart,
]; 

export interface ManageMembershipState {
    loadingSearchResults: boolean;
    searchResults?: DestinationPickerPersona[];
    selectedDestination: Destination | undefined;
    onboardingStatus: OnboardingStatus | null;
    hasChanges: boolean;
    currentStep: number;
    isQueryValid: boolean;
    startDateOption: string;
    useThresholdLimits: string;
    showIncreaseDropdown: boolean;
    showDecreaseDropdown: boolean;
    newJob: NewJob;
    isAdvancedView: boolean;
    compositeQuery?: SyncJobQuery; // Made up of source parts' queries
    advancedViewQuery?: SyncJobQuery;
    sourceParts: ISourcePart[];
}

const initialState: ManageMembershipState = {
    loadingSearchResults: false,
    searchResults: [],
    selectedDestination: undefined,
    onboardingStatus: null,
    hasChanges: false,
    currentStep: 1,
    isQueryValid: false,
    startDateOption: 'ASAP',
    useThresholdLimits: 'Yes',
    showIncreaseDropdown: true,
    showDecreaseDropdown: true,
    newJob: {
        destination: '',
        requestor: '',
        startDate: new Date().toISOString(),
        period: 24,
        query: placeholderAdvancedViewQuery,
        thresholdPercentageForAdditions: 100,
        thresholdPercentageForRemovals: 20,
        status: 'Idle'
    },
    isAdvancedView: false,
    compositeQuery: placeholderAdvancedViewQuery,
    advancedViewQuery: placeholderAdvancedViewQuery,
    sourceParts: []
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
        setIsQueryValid: (state, action: PayloadAction<boolean>) => {
            state.isQueryValid = action.payload;
        },
        setNewJobStartDate: (state, action: PayloadAction<string>) => {
            state.newJob.startDate = action.payload;
        },
        setNewJobPeriod: (state, action: PayloadAction<number>) => {
            state.newJob.period = action.payload;
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
        resetManageMembership: (state) => {
            Object.assign(state, initialState);
        },
        setIsAdvancedView: (state, action: PayloadAction<boolean>) => {
            if (action.payload) {
                // Switching to advanced view
                if (state.compositeQuery) {
                    state.newJob.query = state.compositeQuery;
                }
            } else {
                // Switching from advanced view
                try {
                    const parsedQuery = JSON.parse(state.newJob.query);
                    const updatedSourceParts = parsedQuery.map((part: any, index: number) => {
                        const isExclusionary = part.exclusionary === true; // Default to false if not present
                        if (part.hasOwnProperty('exclusionary')) {
                          delete part.exclusionary;
                        }
                        const queryStr = JSON.stringify(part, null, 2);
                        return {
                          id: index + 1,
                          query: queryStr,
                          isValid: true,
                          isExclusionary: isExclusionary
                        };
                      });
                    
                    state.sourceParts = updatedSourceParts;
                    state.isQueryValid = true;
                } catch (error) {
                    console.error(`Error parsing advanced view query:`, error);
                    state.sourceParts = [];
                    state.isQueryValid = false;
                    state.newJob.query = [placeholderQueryHRPart];
                }
            }
            state.isAdvancedView = action.payload;
        },
        setAdvancedViewQuery: (state, action: PayloadAction<SyncJobQuery | undefined>) => {
            if(!action.payload) return;
            state.advancedViewQuery = action.payload;
            state.newJob.query = action.payload;
        },
        setCompositeQuery: (state, action: PayloadAction<SyncJobQuery>) => {
            state.compositeQuery = action.payload;
        },
        setSourceParts: (state, action: PayloadAction<ISourcePart[]>) => {
            state.sourceParts = action.payload;
        },
        addSourcePart: (state, action: PayloadAction<ISourcePart>) => {
            state.sourceParts.push(action.payload);
        },
        updateSourcePart: (state, action: PayloadAction<ISourcePart>) => {
            const index = state.sourceParts.findIndex(part => part.id === action.payload.id);
            if (index !== -1) {
              state.sourceParts[index] = {
                ...state.sourceParts[index],
                query: action.payload.query,
                isValid: action.payload.isValid,
                isExclusionary: action.payload.isExclusionary
              };
            }
          },                 
        updateSourcePartValidity: (state, action: PayloadAction<{ partId: number; isValid: boolean}>) => {
            const { partId, isValid } = action.payload;
            const partIndex = state.sourceParts.findIndex(part => part.id === partId);
            if (partIndex !== -1) {
                state.sourceParts[partIndex].isValid = isValid;
            }
        },
        deleteSourcePart: (state, action: PayloadAction<number>) => {
            state.sourceParts = state.sourceParts.filter(part => part.id !== action.payload);
            const compositeQuery = buildCompositeQuery(state.sourceParts);
            state.compositeQuery = compositeQuery;
        },
        clearSourceParts: (state) => {
            state.sourceParts = [];
        },
    },
    extraReducers: (builder) => {
        builder.addCase(getGroupOnboardingStatus.fulfilled, (state, action) => {
            state.onboardingStatus = action.payload;
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
        builder.addCase(getGroupEndpoints.fulfilled, (state, action) => {
            if (state.selectedDestination?.id === action.meta.arg) {
                state.selectedDestination.endpoints = action.payload;
            }
        });
    },
});

export const {
    setHasChanges,
    setCurrentStep,
    setNewJobQuery,
    setIsQueryValid,
    setSelectedDestination,
    setNewJobStartDate,
    setNewJobPeriod,
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
    updateSourcePart,
    updateSourcePartValidity,
    deleteSourcePart,
    clearSourceParts,
} = manageMembershipSlice.actions;

// General
export const manageMembershipHasChanges = (state: RootState) => state.manageMembership.hasChanges;
export const manageMembershipCurrentStep = (state: RootState) => state.manageMembership.currentStep;
export const manageMembershipSelectedDestinationType = (state: RootState) => state.manageMembership.selectedDestination?.type;
export const manageMembershipSelectedDestinationName = (state: RootState) => state.manageMembership.selectedDestination?.name;

// Onboarding values
export const manageMembershipSelectedDestination = (state: RootState) => state.manageMembership.selectedDestination;
export const manageMembershipQuery = (state: RootState) => state.manageMembership.newJob.query;
export const manageMembershipStartDate = (state: RootState) => state.manageMembership.newJob.startDate;
export const manageMembershipPeriod = (state: RootState) => state.manageMembership.newJob.period;
export const manageMembershipThresholdPercentageForAdditions = (state: RootState) => state.manageMembership.newJob.thresholdPercentageForAdditions;
export const manageMembershipThresholdPercentageForRemovals = (state: RootState) => state.manageMembership.newJob.thresholdPercentageForRemovals;

// 1- Select Destination
export const manageMembershipSearchResults = (state: RootState) => state.manageMembership.searchResults;
export const manageMembershipLoadingSearchResults = (state: RootState) => state.manageMembership.loadingSearchResults;
export const manageMembershipSelectedDestinationEndpoints = (state: RootState) => state.manageMembership.selectedDestination?.endpoints;
export const manageMembershipGroupOnboardingStatus = (state: RootState) => state.manageMembership.onboardingStatus;
export const manageMembershipIsGroupReadyForOnboarding = (state: RootState): boolean => {
    return state.manageMembership.onboardingStatus === OnboardingStatus.ReadyForOnboarding;
};

// 2- Membership Configuration
export const manageMembershipIsAdvancedView = (state: RootState) => state.manageMembership.isAdvancedView;
export const manageMembershipIsQueryValid = (state: RootState) => state.manageMembership.isQueryValid;
export const manageMembershipCompositeQuery = (state: RootState) => state.manageMembership.compositeQuery;
export const manageMembershipAdvancedViewQuery = (state: RootState) => state.manageMembership.advancedViewQuery;
export const getSourcePartsFromState = (state: RootState) => state.manageMembership.sourceParts;
export const manageMembershipIsToggleEnabled = (state: RootState) => {
    const isAdvancedView = state.manageMembership.isAdvancedView;
    const isAdvancedViewQueryValid = state.manageMembership.isQueryValid;
    const advancedViewQuery = state.manageMembership.advancedViewQuery;
    const areAllSourcePartsValid = state.manageMembership.sourceParts.every(part => part.isValid);
    const isPlaceholderQuery = advancedViewQuery === placeholderAdvancedViewQuery;
    return isAdvancedView ? (isAdvancedViewQueryValid || isPlaceholderQuery) : areAllSourcePartsValid;
};


// 3- Run Configuration
export const manageMembershipStartDateOption = (state: RootState) => state.manageMembership.startDateOption;
export const manageMembershipUseThresholdLimits = (state: RootState) => state.manageMembership.useThresholdLimits;
export const manageMembershipShowIncreaseDropdown = (state: RootState) => state.manageMembership.showIncreaseDropdown;
export const manageMembershipShowDecreaseDropdown = (state: RootState) => state.manageMembership.showDecreaseDropdown;

export default manageMembershipSlice.reducer;

export function buildCompositeQuery(sourceParts: ISourcePart[]): SyncJobQuery {
    const queries = sourceParts.map(part => {
        try {
            const hrSourcePart = part.query;
            return {
                ...hrSourcePart,
                exclusionary: part.isExclusionary
            };
        } catch (error) {
            console.error(`Error processing HRSourcePart for part ${part.id}:`, error);
            return null;
        }
    }).filter(query => query !== null);
    return queries as SyncJobQuery;
}
