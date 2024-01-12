// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { useDispatch } from 'react-redux';
import { SourcePartProps } from './SourcePart.types';
import { AppDispatch } from '../../store';
import { SourcePartView } from './SourcePart.view';

export const SourcePartBase: React.FunctionComponent<SourcePartProps> = (props: SourcePartProps) => {
  const dispatch = useDispatch<AppDispatch>();
  // const strings = useStrings().SourcePart;

  return (
    <SourcePartView
      {...props}
      index={props.index}
      query={props.query}
      totalSourceParts={props.totalSourceParts}
      onDelete={props.onDelete}
    />
  );
};
