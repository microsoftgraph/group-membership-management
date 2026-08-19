// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import * as React from 'react';
import { mergeStyles, keyframes } from '@fluentui/react';

const sparkleAnimation1 = keyframes({
  '0%, 100%': { opacity: 1, transform: 'scale(1)' },
  '50%': { opacity: 0.2, transform: 'scale(0.5)' },
});

const sparkleAnimation2 = keyframes({
  '0%, 100%': { opacity: 0.3, transform: 'scale(0.5)' },
  '50%': { opacity: 1, transform: 'scale(1)' },
});

const animatedSparkleClass1 = mergeStyles({
  animationName: sparkleAnimation1,
  animationDuration: '2s',
  animationTimingFunction: 'ease-in-out',
  animationIterationCount: 'infinite',
  transformOrigin: 'center',
  transformBox: 'fill-box',
});

const animatedSparkleClass2 = mergeStyles({
  animationName: sparkleAnimation2,
  animationDuration: '2s',
  animationTimingFunction: 'ease-in-out',
  animationIterationCount: 'infinite',
  transformOrigin: 'center',
  transformBox: 'fill-box',
});

export interface ISparkleIconProps {
  size?: number;
  animated?: boolean;
  className?: string;
}

/**
 * Shared sparkle glyph used to mark AI-powered features (Copilot trigger, AI feedback refinement).
 */
export const SparkleIcon: React.FunctionComponent<ISparkleIconProps> = ({
  size = 16,
  animated = true,
  className,
}) => (
  <svg
    width={size}
    height={size}
    viewBox="0 0 16 16"
    fill="none"
    className={className}
    aria-hidden="true"
    focusable="false"
  >
    <path
      className={animated ? animatedSparkleClass1 : undefined}
      d="M8 4 L9 7 L12 8 L9 9 L8 12 L7 9 L4 8 L7 7 Z"
      fill="currentColor"
    />
    <path
      className={animated ? animatedSparkleClass2 : undefined}
      d="M13 1 L13.5 2.5 L15 3 L13.5 3.5 L13 5 L12.5 3.5 L11 3 L12.5 2.5 Z"
      fill="currentColor"
    />
  </svg>
);
