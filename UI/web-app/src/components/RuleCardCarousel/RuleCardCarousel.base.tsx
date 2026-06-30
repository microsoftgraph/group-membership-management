// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useCallback, useEffect, useRef, useState } from 'react';
import { classNamesFunction, IProcessedStyleSet, IconButton } from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import {
  RuleCardCarouselStyleProps,
  RuleCardCarouselStyles,
  RuleCardCarouselProps,
} from './RuleCardCarousel.types';
import { RuleCard } from '../RuleCard';
import { useStrings } from '../../store/hooks';

const getClassNames = classNamesFunction<RuleCardCarouselStyleProps, RuleCardCarouselStyles>();

const SCROLL_AMOUNT = 300;

export const RuleCardCarouselBase: React.FunctionComponent<RuleCardCarouselProps> = (
  props: RuleCardCarouselProps
) => {
  const { className, styles, parts, selectedPartId, onSelectPart, showCaret = true } = props;
  const classNames: IProcessedStyleSet<RuleCardCarouselStyles> = getClassNames(styles, {
    className,
    theme: useTheme(),
  });
  const strings = useStrings();
  const ruleStrings = strings.Components.RuleCard;

  const trackRef = useRef<HTMLDivElement>(null);
  const rootRef = useRef<HTMLDivElement>(null);
  const [canScrollLeft, setCanScrollLeft] = useState(false);
  const [canScrollRight, setCanScrollRight] = useState(false);
  const [caretLeft, setCaretLeft] = useState<number | null>(null);

  const updateCaret = useCallback(() => {
    const root = rootRef.current;
    const track = trackRef.current;
    if (!root || !track) return;
    const index = parts.findIndex((p) => p.id === selectedPartId);
    const cardEl = index >= 0 ? (track.children[index] as HTMLElement | undefined) : undefined;
    if (!cardEl) {
      setCaretLeft(null);
      return;
    }
    const rootRect = root.getBoundingClientRect();
    const trackRect = track.getBoundingClientRect();
    const cardRect = cardEl.getBoundingClientRect();
    const center = cardRect.left + cardRect.width / 2;
    // Hide the caret when the selected card is scrolled out of the track viewport.
    if (center < trackRect.left || center > trackRect.right) {
      setCaretLeft(null);
      return;
    }
    setCaretLeft(center - rootRect.left);
  }, [parts, selectedPartId]);

  const updateScrollState = useCallback(() => {
    const el = trackRef.current;
    if (!el) return;
    setCanScrollLeft(el.scrollLeft > 0);
    setCanScrollRight(Math.ceil(el.scrollLeft + el.clientWidth) < el.scrollWidth);
    updateCaret();
  }, [updateCaret]);

  useEffect(() => {
    updateScrollState();
    const el = trackRef.current;
    if (!el) return;
    el.addEventListener('scroll', updateScrollState);
    window.addEventListener('resize', updateScrollState);
    return () => {
      el.removeEventListener('scroll', updateScrollState);
      window.removeEventListener('resize', updateScrollState);
    };
  }, [updateScrollState, parts.length]);

  // Reposition the caret when the selection changes.
  useEffect(() => {
    updateCaret();
  }, [updateCaret]);

  const scrollBy = (amount: number) => {
    trackRef.current?.scrollBy({ left: amount, behavior: 'smooth' });
  };

  return (
    <div className={classNames.root} ref={rootRef}>
      <IconButton
        className={classNames.scrollButton}
        iconProps={{ iconName: 'ChevronLeft' }}
        ariaLabel={ruleStrings.scrollLeft}
        title={ruleStrings.scrollLeft}
        disabled={!canScrollLeft}
        onClick={() => scrollBy(-SCROLL_AMOUNT)}
      />
      <div className={classNames.track} ref={trackRef}>
        {parts.map((part) => (
          <RuleCard
            key={part.id}
            part={part}
            selected={part.id === selectedPartId}
            onSelect={onSelectPart}
          />
        ))}
      </div>
      <IconButton
        className={classNames.scrollButton}
        iconProps={{ iconName: 'ChevronRight' }}
        ariaLabel={ruleStrings.scrollRight}
        title={ruleStrings.scrollRight}
        disabled={!canScrollRight}
        onClick={() => scrollBy(SCROLL_AMOUNT)}
      />
      {showCaret && caretLeft !== null && (
        <div className={classNames.caret} style={{ left: caretLeft }} aria-hidden="true" />
      )}
    </div>
  );
};
