import { render, fireEvent } from '@testing-library/react';
import React from 'react';
import { Provider, useSelector } from 'react-redux';
import configureStore from 'redux-mock-store';

import { OwnerBase } from './Owner.base';
import { type RootState } from '../../store';
import { addOwner } from '../../store/owner.api';
import { type OwnerState, selectOwner } from '../../store/owner.slice';

const mockStore = configureStore([]);
const initialState = { owner: { status: '' } };
const store = mockStore(initialState);

describe('OwnerBase component', () => {
  it('renders without errors', () => {
    const { getByText } = render(
      <Provider store={store}>
        <OwnerBase />
      </Provider>
    );
    expect(getByText('groupIdHeader')).toBeInTheDocument();
  });

  it('dispatches addOwner action when button is clicked', () => {
    const { getByLabelText, getByText } = render(
      <Provider store={store}>
        <OwnerBase />
      </Provider>
    );

    const input = getByLabelText('groupIdPlaceHolder');
    const button = getByText('okButton');
    fireEvent.change(input, { target: { value: 'test' } });
    fireEvent.click(button);
    const expectedAction = addOwner('test');
    expect(store.getActions()).toContainEqual(expectedAction);
  });

  it('displays error message when add owner status is 403', () => {
    const { getByText } = render(
      <Provider store={store}>
        <OwnerBase />
      </Provider>
    );

    const selectOwner = (state: { owner: OwnerState }) => state.owner;
    const owner = useSelector((state: RootState) => selectOwner(state));
    const action = {
      type: owner.status,
      payload: { status: 'false 403 Forbidden' },
    };
    store.dispatch(action);
    expect(getByText('addOwner403Message')).toBeInTheDocument();
  });

  it('displays error message when add owner status is 400', () => {
    const { getByText } = render(
      <Provider store={store}>
        <OwnerBase />
      </Provider>
    );

    const selectOwner = (state: { owner: OwnerState }) => state.owner;
    const owner = useSelector((state: RootState) => selectOwner(state));
    const action = {
      type: owner.status,
      payload: { status: 'false 400 Bad Request' },
    };
    store.dispatch(action);
    expect(getByText('addOwner400Message')).toBeInTheDocument();
  });

  it('displays error message when add owner status is 204', () => {
    const { getByText } = render(
      <Provider store={store}>
        <OwnerBase />
      </Provider>
    );

    const selectOwner = (state: { owner: OwnerState }) => state.owner;
    const owner = useSelector((state: RootState) => selectOwner(state));
    const action = {
      type: owner.status,
      payload: { status: 'true 204 No Content' },
    };
    store.dispatch(action);
    expect(getByText('addOwner204Message')).toBeInTheDocument();
  });
});
