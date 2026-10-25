// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createTheme, type ITheme } from '@fluentui/react';
import { describe, expect, it } from 'vitest';
import { contrastRatio } from '../../../tests/utils/contrastRatio';
import { getAccessibleStatusColors, isDarkTheme } from '../../theme/accessibleColors';
import { darkPalette } from '../../theme/palette';
import { getStyles } from './AdminConfig.styles';

const themes: Array<[string, ITheme]> = [
  ['light', createTheme({})],
  ['dark', createTheme({ palette: darkPalette })],
];

describe('AdminConfig dashboard styles', () => {
  it.each(themes)('%s snapshot metadata remains readable beside Refresh', (_mode, theme) => {
    const styles = getStyles({ theme });

    expect(styles.dashboardSnapshot).toMatchObject({ color: theme.palette.neutralSecondary });
    expect(contrastRatio(theme.palette.neutralSecondary, theme.palette.white)).toBeGreaterThanOrEqual(4.5);
  });

  it('keeps the timestamp and Refresh together while the toolbar can wrap', () => {
    const styles = getStyles({ theme: themes[0][1] });

    expect(styles.dashboardToolbar).toMatchObject({ flexWrap: 'wrap', minWidth: 0, maxWidth: '100%' });
    expect(styles.dashboardRefresh).toMatchObject({
      display: 'flex',
      alignItems: 'center',
      minWidth: 0,
      maxWidth: '100%',
    });
  });

  it.each(themes)('%s count strips use readable neutral text on a distinct surface', (_mode, theme) => {
    const styles = getStyles({ theme });
    const background = isDarkTheme(theme) ? theme.palette.neutralLight : theme.palette.neutralLighter;

    expect(styles.dashboardSection).toMatchObject({ backgroundColor: theme.palette.white });
    expect(styles.dashboardCount).toMatchObject({ backgroundColor: background });
    expect(background).not.toBe(theme.palette.white);
    expect(styles.dashboardCountLabel).toMatchObject({
      color: theme.palette.neutralPrimary,
      minWidth: 0,
      maxWidth: '100%',
      overflow: 'hidden',
      textOverflow: 'ellipsis',
      whiteSpace: 'nowrap',
    });
    expect(styles.dashboardCountValue).toMatchObject({ color: theme.palette.neutralPrimary, flexShrink: 0 });
    expect(contrastRatio(theme.palette.neutralPrimary, background)).toBeGreaterThanOrEqual(4.5);
  });

  it.each(themes)('%s headings and breakdown links use accessible theme accents', (_mode, theme) => {
    const styles = getStyles({ theme });
    const { accentText } = getAccessibleStatusColors(theme);

    expect(styles.dashboardHeading).toMatchObject({ color: accentText });
    expect(styles.dashboardBreakdown).toMatchObject({ selectors: { summary: { color: accentText } } });
    expect(contrastRatio(accentText, theme.palette.white)).toBeGreaterThanOrEqual(4.5);
  });

  it.each(themes)('%s info icons use a help cursor without changing their theme color', (_mode, theme) => {
    expect(getStyles({ theme }).dashboardInfoIcon).toMatchObject({
      cursor: 'help',
      color: theme.palette.neutralPrimary,
    });
  });

  it.each(themes)('%s table rows retain neutral rather than severity-colored text', (_mode, theme) => {
    expect(getStyles({ theme }).dashboardTable).toMatchObject({
      selectors: { '.ms-DetailsRow': { color: theme.palette.neutralPrimary } },
    });
  });

  it('recognizes the app dark palette without relying on Fluent isInverted', () => {
    const theme = createTheme({ palette: darkPalette });

    expect(theme.isInverted).not.toBe(true);
    expect(getStyles({ theme }).dashboardCount).toMatchObject({
      backgroundColor: theme.palette.neutralLight,
    });
  });

  it('uses the same contrast calculation for palette hex and computed browser RGB colors', () => {
    expect(contrastRatio('#ffffff', '#000000')).toBe(21);
    expect(contrastRatio('rgb(255, 255, 255)', 'rgb(0, 0, 0)')).toBe(21);
    expect(contrastRatio('#323130', 'rgb(50, 49, 48)')).toBe(1);
    expect(() => contrastRatio('rgba(0, 0, 0, 0)', '#ffffff')).toThrow();
    expect(() => contrastRatio('rgb(256, 0, 0)', '#ffffff')).toThrow();
  });
});
