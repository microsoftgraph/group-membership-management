// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import * as React from 'react';
import { useDispatch, useSelector } from 'react-redux';
import { useLocation, useParams } from 'react-router-dom';
import { mergeStyles, useTheme } from '@fluentui/react';
import { SparkleIcon } from '../SparkleIcon';
import { AppDispatch } from '../../store';
import { useStrings } from '../../store/hooks';
import { selectIsJobWriter } from '../../store/roles.slice';
import { openPanel, selectCopilotMessages, selectIsCopilotAvailable } from '../../store/copilot.slice';

const getCopilotButtonClass = (theme: ReturnType<typeof useTheme>) => mergeStyles({
  display: 'flex',
  alignItems: 'center',
  gap: '8px',
  height: 32,
  padding: '0 11px',
  borderRadius: 4,
  border: '2px solid transparent',
  backgroundImage: `linear-gradient(#F4F4FC, #F4F4FC), linear-gradient(90deg, #0086F0, #8C90F4, #82C7FF)`,
  backgroundOrigin: 'border-box',
  backgroundClip: 'padding-box, border-box',
  color: '#464775',
  fontFamily: 'Segoe UI',
  fontSize: 14,
  fontWeight: 400,
  lineHeight: '20px',
  letterSpacing: '0%',
  cursor: 'pointer',
  whiteSpace: 'nowrap',
  transition: 'box-shadow 0.2s ease, border-color 0.2s ease',
  selectors: {
    ':hover': {
      backgroundImage: `linear-gradient(#ECECFA, #ECECFA), linear-gradient(90deg, #0086F0, #8C90F4, #82C7FF)`,
    },
    ':disabled': {
      opacity: 0.5,
      cursor: 'not-allowed',
    },
    // Respect Windows/High-contrast themes: fall back to system colors instead of
    // hard-coded brand hex/gradients, which are not guaranteed to render in forced-colors mode.
    '@media (forced-colors: active)': {
      backgroundImage: 'none',
      backgroundColor: 'ButtonFace',
      border: '2px solid ButtonText',
      color: 'ButtonText',
      selectors: {
        ':hover': {
          backgroundColor: 'Highlight',
          color: 'HighlightText',
        },
      },
    },
  },
});

export const CopilotTriggerButton: React.FunctionComponent<{
  disabled?: boolean;
  icon?: React.ReactNode;
}> = ({ disabled = false, icon }) => {
  const dispatch = useDispatch<AppDispatch>();
  const strings = useStrings();
  const theme = useTheme();
  const isJobWriter = useSelector(selectIsJobWriter);
  const isCopilotAvailable = useSelector(selectIsCopilotAvailable);
  const hasCopilotHistory = useSelector(selectCopilotMessages).length > 0;

  // Derive edit-state synchronously from the route (mirrors the parent's jobId derivation)
  // so the button never flashes for one render before the parent effect dispatches
  // setIsEditingExistingJob(true) on direct navigation into the edit flow.
  const location = useLocation();
  const { jobId: urlJobId } = useParams<{ jobId: string }>();
  const locationState = location.state as { jobId?: string } | undefined;
  const isEditingExistingJob = !!(locationState?.jobId ?? urlJobId);

  if (!isCopilotAvailable) return null;

  return (
    <button
      onClick={() => dispatch(openPanel())}
      type="button"
      disabled={disabled || !isJobWriter}
      data-testid="copilot-trigger-button"
      className={getCopilotButtonClass(theme)}
    >
      {icon ?? <SparkleIcon />}
      <span>
        {hasCopilotHistory || isEditingExistingJob
          ? (strings.Copilot?.triggerButtonResume || 'Let Copilot resume building it for you')
          : (strings.Copilot?.triggerButton || 'Let Copilot build it for you')}
      </span>
    </button>
  );
};
