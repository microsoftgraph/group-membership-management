// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useState } from 'react';
import { classNamesFunction, type IProcessedStyleSet, DefaultButton, PrimaryButton } from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import type {
  OperationProps,
  OperationStyleProps,
  OperationStyles,
} from './Operation.types';
import { useStrings } from '../../store/hooks';

export const getClassNames = classNamesFunction<OperationStyleProps, OperationStyles>();

export const OperationBase: React.FunctionComponent<OperationProps> = (props: OperationProps) => {
  const { title, className, styles } = props;
  const classNames: IProcessedStyleSet<OperationStyles> = getClassNames(styles, {
    className,
    theme: useTheme(),
  });
  const strings = useStrings();

  const [isStopped, setIsStopped] = useState(false);
  const [isRestarting, setIsRestarting] = useState(false);

  const handleStop = () => {
    setIsStopped(true);
    setIsRestarting(false);
  };

  const handleRestart = () => {
    setIsRestarting(true);
    setIsStopped(false);
  };

return (
  <div className={classNames.card}>
    <div className={classNames.title}>{title}</div>
    <div className={classNames.buttonContainer}>
      <DefaultButton
        text={isStopped ? 'Stopped' : 'Stop GMM'}
        onClick={handleStop}
        disabled={isStopped}
        className={classNames.button}
      />
      <DefaultButton
        text={isRestarting ? 'Restarting...' : 'Restart GMM'}
        onClick={handleRestart}
        disabled={isRestarting}
        className={classNames.button}
      />
    </div>
  </div>
);
};
