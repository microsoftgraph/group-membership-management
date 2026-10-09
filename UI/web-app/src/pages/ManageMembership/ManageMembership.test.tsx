// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useState } from 'react';
import { act, fireEvent, screen } from '@testing-library/react';
import { Link, MemoryRouter, Route, Routes } from 'react-router-dom';
import { useSelector } from 'react-redux';
import { describe, expect, it, vi } from 'vitest';
import { ManageMembership } from './ManageMembership';
import { renderWithProviders } from '../../testing/renderWithProviders';
import copilotReducer, { addMessage, closePanel, openPanel, type CopilotState } from '../../store/copilot.slice';
import { sendCopilotMessage } from '../../store/copilot.api';
import type { RootState } from '../../store';

vi.mock('./ManageMembership.base', () => ({
  ManageMembershipBase: () => {
    const messages = useSelector((state: RootState) => state.copilot.messages);
    const [draft, setDraft] = useState('');
    return (
      <>
        <div data-testid="transcript">{messages.map(message => message.content).join('\n')}</div>
        <input aria-label="Local panel draft" value={draft} onChange={event => setDraft(event.target.value)} />
      </>
    );
  },
}));

const message = {
  id: 'old-message',
  role: 'user' as const,
  content: 'Previous membership conversation',
  timestamp: '2026-10-08T18:00:00Z',
};

const renderSession = (initialEntry: string) => {
  const copilot: CopilotState = {
    ...copilotReducer(undefined, { type: '@@INIT' }),
    messages: [message],
    conversationId: 'previous-session',
    modelContext: { startIndex: 1, summary: 'Previous membership description' },
    isPanelOpen: true,
    isLoading: true,
    currentRequestId: 'old-request',
    lastAppliedOperations: [{ op: 'set' }],
    error: 'Previous error',
    warning: 'Previous warning',
  };
  return renderWithProviders(
    <MemoryRouter initialEntries={[initialEntry]}>
      <Link to="/">Main page</Link>
      <Link to="/ManageMembership">Add</Link>
      <Link to="/ManageMembership/job-a">Edit A</Link>
      <Link to="/ManageMembership/job-b">Edit B</Link>
      <Routes>
        <Route path="/" element={<div>Main page content</div>} />
        <Route path="/ManageMembership" element={<ManageMembership />} />
        <Route path="/ManageMembership/:jobId" element={<ManageMembership />} />
      </Routes>
    </MemoryRouter>,
    { preloadedState: { copilot } }
  );
};

const expectFreshSession = (state: CopilotState) => {
  expect(state.messages).toEqual([]);
  expect(state.modelContext).toBeUndefined();
  expect(state.currentRequestId).toBeUndefined();
  expect(state.isLoading).toBe(false);
  expect(state.isPanelOpen).toBe(false);
  expect(state.lastSourceParts).toEqual([]);
  expect(state.lastAppliedOperations).toEqual([]);
  expect(state.error).toBeNull();
  expect(state.warning).toBeNull();
  expect(screen.getByTestId('transcript')).toBeEmptyDOMElement();
  expect(screen.getByLabelText('Local panel draft')).toHaveValue('');
};

describe('ManageMembership Copilot session boundary', () => {
  it('starts fresh when Add is clicked on the main page', () => {
    const { store } = renderSession('/');
    fireEvent.click(screen.getByRole('link', { name: 'Add' }));
    expectFreshSession(store.getState().copilot);
    expect(store.getState().copilot.conversationId).not.toBe('previous-session');
  });

  it('starts fresh when entering an editing deep link directly', () => {
    const { store } = renderSession('/managemembership/job-a');
    expectFreshSession(store.getState().copilot);
  });

  it('discards Redux and local panel state when switching jobs or reentering the same job', () => {
    const { store } = renderSession('/ManageMembership/job-a');
    for (const target of ['Edit B', 'Edit B', 'Add']) {
      act(() => {
        store.dispatch(addMessage(message));
        store.dispatch(openPanel());
      });
      fireEvent.change(screen.getByLabelText('Local panel draft'), { target: { value: 'Unsent draft' } });
      const previousId = store.getState().copilot.conversationId;
      fireEvent.click(screen.getByRole('link', { name: target }));
      expectFreshSession(store.getState().copilot);
      expect(store.getState().copilot.conversationId).not.toBe(previousId);
    }
  });

  it('preserves conversation and draft across panel close/reopen in the same session', () => {
    const { store } = renderSession('/ManageMembership/job-a');
    const conversationId = store.getState().copilot.conversationId;
    act(() => {
      store.dispatch(addMessage(message));
      store.dispatch(openPanel());
    });
    fireEvent.change(screen.getByLabelText('Local panel draft'), { target: { value: 'Keep draft' } });
    act(() => {
      store.dispatch(closePanel());
      store.dispatch(openPanel());
    });
    expect(store.getState().copilot.messages).toEqual([message]);
    expect(store.getState().copilot.conversationId).toBe(conversationId);
    expect(screen.getByLabelText('Local panel draft')).toHaveValue('Keep draft');
  });

  it('invalidates an in-flight response on exit so it cannot populate the next session', () => {
    const { store } = renderSession('/ManageMembership/job-a');
    const args = { message: 'Old request', hrAttributes: [] };
    act(() => {
      store.dispatch(sendCopilotMessage.pending('in-flight', args));
    });
    fireEvent.click(screen.getByRole('link', { name: 'Main page' }));
    expect(store.getState().copilot.currentRequestId).toBeUndefined();
    fireEvent.click(screen.getByRole('link', { name: 'Add' }));
    act(() => {
      store.dispatch(sendCopilotMessage.fulfilled({
        message,
        sourceParts: [],
        useOrgStructure: false,
        appliedOperations: [{ op: 'set' }],
      }, 'in-flight', args));
      store.dispatch(sendCopilotMessage.rejected(new Error('Old failure'), 'in-flight', args));
    });
    expectFreshSession(store.getState().copilot);
  });
});
