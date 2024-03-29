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

const placeholderQuery: string = `[
    {
      "type": "GroupMembership",
      "source": "",
      "exclusionary": false
    },
    {
      "type": "SqlMembership",
      "source": {
        "ids": [],
        "filter": "",
        "depth": 1
      },
      "exclusionary": false
    },
    {
      "type": "GroupOwnership",
      "source": [ "" ],
      "exclusionary": false
    }
  ]`;

export interface ManageMembershipState {
    loadingSearchResults: boolean;
    searchResults?: Destination[] | undefined;
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
        query: placeholderQuery,
        thresholdPercentageForAdditions: 100,
        thresholdPercentageForRemovals: 20,
        status: 'Idle'
    }
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
        setSearchResults: (state, action: PayloadAction<Destination[]>) => {
            state.searchResults = action.payload;
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
        setNewJobQuery: (state, action: PayloadAction<string>) => {
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
        }
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
            if(state.selectedDestination?.id === action.meta.arg) {
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
    setStartDateOption,
    setUseThresholdLimits,
    setShowIncreaseDropdown,
    setShowDecreaseDropdown,
    resetManageMembership
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
export const manageMembershipIsGroupReadyForOnboarding = (state: RootState): boolean => {
    return state.manageMembership.onboardingStatus?.toString() === OnboardingStatus.ReadyForOnboarding.toString();
};

// 2- Advanced Query
export const manageMembershipIsQueryValid = (state: RootState) => state.manageMembership.isQueryValid;

// 3- Run Configuration
export const manageMembershipStartDateOption = (state: RootState) => state.manageMembership.startDateOption;
export const manageMembershipUseThresholdLimits = (state: RootState) => state.manageMembership.useThresholdLimits;
export const manageMembershipShowIncreaseDropdown = (state: RootState) => state.manageMembership.showIncreaseDropdown;
export const manageMembershipShowDecreaseDropdown = (state: RootState) => state.manageMembership.showDecreaseDropdown;

export default manageMembershipSlice.reducer;
