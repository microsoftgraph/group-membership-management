// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { NeutralColors, type ITheme } from '@fluentui/react';

/**
 * The app's dark theme (see theme/palette.ts) only inverts the neutral ramp; it is not created with
 * `isInverted: true`. As a result Fluent keeps handing back the *light* status/accent colors
 * (e.g. `semanticColors.warningBackground` = #FFF4CE, `semanticColors.errorIcon` = #A80000), which
 * render light-on-light or dark-on-dark once the surface behind them is near-black.
 *
 * The values below are the exact colors Fluent uses for an inverted theme
 * (see @fluentui/theme `makeSemanticColors`), so dark mode gets WCAG AA contrast while light mode
 * keeps its original Fluent styling untouched.
 */
export const isDarkTheme = (theme: ITheme): boolean =>
    theme.palette.white.toLowerCase() === NeutralColors.gray220.toLowerCase();

export interface IAccessibleStatusColors {
    /** Foreground for error/negative text and icons. */
    errorText: string;
    /** Surface behind error/negative content. */
    errorBackground: string;
    /** Foreground for success/positive text and icons. */
    successText: string;
    /** Surface behind success/positive content. */
    successBackground: string;
    /** Surface behind warning content. */
    warningBackground: string;
    /** Foreground for warning icons. */
    warningIcon: string;
    /** Accent color usable as text/link/icon on the current surface. */
    accentText: string;
    /** Hover/active variant of {@link accentText}. */
    accentTextHovered: string;
}

const invertedStatusColors: IAccessibleStatusColors = {
    errorText: '#F1707B',
    errorBackground: '#442726',
    successText: '#92C353',
    successBackground: '#393D1B',
    warningBackground: '#433519',
    warningIcon: '#FCE100',
    accentText: '#6CB8F6',
    accentTextHovered: '#82C7FF',
};

/** Status/accent colors Fluent uses for inverted (dark) themes. */
export const darkModeStatusColors: IAccessibleStatusColors = invertedStatusColors;

/**
 * Returns status/accent colors that meet contrast requirements against the current theme's surfaces.
 * In light mode these are the untouched Fluent defaults.
 */
export const getAccessibleStatusColors = (theme: ITheme): IAccessibleStatusColors => {
    if (isDarkTheme(theme)) {
        return invertedStatusColors;
    }

    return {
        errorText: theme.semanticColors.errorIcon,
        errorBackground: theme.semanticColors.errorBackground,
        successText: theme.semanticColors.successIcon,
        successBackground: theme.semanticColors.successBackground,
        warningBackground: theme.semanticColors.warningBackground,
        warningIcon: theme.palette.neutralPrimary,
        accentText: theme.palette.themePrimary,
        accentTextHovered: theme.palette.themeDarker,
    };
};
