// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { describe, expect, it } from 'vitest';
import { createTheme, type ITheme } from '@fluentui/react';
import { darkPalette } from '../../theme/palette';
import { getStyles } from './JobHistoryPanel.styles';
import { contrastRatio } from '../../../tests/utils/contrastRatio';

const lightTheme = createTheme({});
const darkTheme = createTheme({ palette: darkPalette });

const styleOf = (theme: ITheme, key: keyof ReturnType<typeof getStyles>): Record<string, string> =>
    getStyles({ theme })[key] as unknown as Record<string, string>;

const TEXT_MIN_CONTRAST = 4.5;
const NON_TEXT_MIN_CONTRAST = 3;

describe('JobHistoryPanel styles contrast', () => {
    const surfaces: Array<[string, ITheme]> = [
        ['light', lightTheme],
        ['dark', darkTheme],
    ];

    it.each(surfaces)('%s mode search result highlight pills meet AA text contrast', (_name, theme) => {
        const added = styleOf(theme, 'highlightedAddedCell');
        const removed = styleOf(theme, 'highlightedRemovedCell');

        expect(contrastRatio(added.color, added.backgroundColor)).toBeGreaterThanOrEqual(TEXT_MIN_CONTRAST);
        expect(contrastRatio(removed.color, removed.backgroundColor)).toBeGreaterThanOrEqual(TEXT_MIN_CONTRAST);
    });

    it.each(surfaces)('%s mode status text meets AA contrast against the panel surface', (_name, theme) => {
        const surface = theme.semanticColors.bodyBackground;

        expect(
            contrastRatio(styleOf(theme, 'statusCellThresholdExceeded').color, surface)
        ).toBeGreaterThanOrEqual(TEXT_MIN_CONTRAST);
        expect(
            contrastRatio(styleOf(theme, 'statusCellThresholdApproved').color, surface)
        ).toBeGreaterThanOrEqual(TEXT_MIN_CONTRAST);
        expect(contrastRatio(styleOf(theme, 'inlineErrorText').color, surface)).toBeGreaterThanOrEqual(
            TEXT_MIN_CONTRAST
        );
        expect(contrastRatio(styleOf(theme, 'clearFiltersLink').color, surface)).toBeGreaterThanOrEqual(
            TEXT_MIN_CONTRAST
        );
    });

    it.each(surfaces)('%s mode change type swatches meet AA non-text contrast', (_name, theme) => {
        const surface = theme.semanticColors.bodyBackground;

        for (const key of ['changeTypeRejected', 'changeTypeApproved', 'changeTypeUpdate', 'changeTypeGroupSettings'] as const) {
            expect(contrastRatio(styleOf(theme, key).backgroundColor, surface)).toBeGreaterThanOrEqual(
                NON_TEXT_MIN_CONTRAST
            );
        }
    });

    it.each(surfaces)('%s mode search banners keep readable text on their surfaces', (_name, theme) => {
        const neutralBanner = styleOf(theme, 'userSearchBanner');
        const warningBanner = styleOf(theme, 'userSearchBannerWarning');
        const bannerText = styleOf(theme, 'userSearchBannerText').color;
        const bannerNote = styleOf(theme, 'userSearchBannerNote').color;

        for (const background of [neutralBanner.backgroundColor, warningBanner.backgroundColor]) {
            expect(contrastRatio(bannerText, background)).toBeGreaterThanOrEqual(TEXT_MIN_CONTRAST);
            expect(contrastRatio(bannerNote, background)).toBeGreaterThanOrEqual(TEXT_MIN_CONTRAST);
        }

        expect(
            contrastRatio(styleOf(theme, 'userSearchBannerIconSuccess').color, neutralBanner.backgroundColor)
        ).toBeGreaterThanOrEqual(NON_TEXT_MIN_CONTRAST);
        expect(
            contrastRatio(styleOf(theme, 'userSearchBannerIconWarning').color, warningBanner.backgroundColor)
        ).toBeGreaterThanOrEqual(NON_TEXT_MIN_CONTRAST);
    });

    it('keeps light mode styling on the original Fluent accent/status colors', () => {
        expect(styleOf(lightTheme, 'userSearchBanner').backgroundColor).toBe(lightTheme.palette.themeLighterAlt);
        expect(styleOf(lightTheme, 'userSearchBannerWarning').backgroundColor).toBe(
            lightTheme.semanticColors.warningBackground
        );
        expect(styleOf(lightTheme, 'clearFiltersLink').color).toBe(lightTheme.palette.themePrimary);
        expect(styleOf(lightTheme, 'highlightedAddedCell').color).toBe(lightTheme.semanticColors.successIcon);
        expect(styleOf(lightTheme, 'highlightedRemovedCell').color).toBe(lightTheme.semanticColors.errorIcon);
        expect(styleOf(lightTheme, 'changeTypeUpdate').backgroundColor).toBe(lightTheme.palette.themePrimary);
    });
});
