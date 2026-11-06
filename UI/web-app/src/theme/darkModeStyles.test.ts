// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createTheme } from '@fluentui/react';
import { describe, expect, it } from 'vitest';
import { getStyles as getManageMembershipStyles } from '../pages/ManageMembership/ManageMembership.styles';
import { getStyles as getMembershipConfigurationStyles } from '../components/MembershipConfiguration/MembershipConfiguration.styles';
import { getStyles as getRuleCardStyles } from '../components/RuleCard/RuleCard.styles';
import { getUserMessageColors } from '../components/CopilotPanel/CopilotPanel.styles';
import { getAccessibleStatusColors } from './accessibleColors';
import { darkPalette } from './palette';
import { contrastRatio } from '../../tests/utils/contrastRatio';

const NON_TEXT_MIN_CONTRAST = 3;
const NORMAL_TEXT_MIN_CONTRAST = 4.5;

describe('dark mode component styles', () => {
  const lightTheme = createTheme();
  const darkTheme = createTheme({ palette: darkPalette });

  it('uses a raised surface for membership cards and the group pill only in dark mode', () => {
    expect(getManageMembershipStyles({ theme: darkTheme }).groupPill).toHaveProperty(
      'backgroundColor',
      darkTheme.palette.neutralLighterAlt
    );
    expect(getMembershipConfigurationStyles({ theme: darkTheme }).addButtonContainer).toHaveProperty(
      'backgroundColor',
      darkTheme.palette.neutralLighterAlt
    );
    expect(getRuleCardStyles({ theme: darkTheme }).root).toEqual(
      expect.arrayContaining([
        expect.objectContaining({
          backgroundColor: darkTheme.palette.neutralLighterAlt,
          border: `1px solid ${darkTheme.palette.neutralTertiaryAlt}`,
        }),
      ])
    );
    expect(
      contrastRatio(darkTheme.palette.neutralTertiaryAlt, darkTheme.palette.neutralLighterAlt)
    ).toBeGreaterThanOrEqual(NON_TEXT_MIN_CONTRAST);
    expect(getRuleCardStyles({ theme: darkTheme }).hiddenText).toHaveProperty(
      'color',
      getAccessibleStatusColors(darkTheme).errorText
    );

    expect(getManageMembershipStyles({ theme: lightTheme }).groupPill).toHaveProperty(
      'backgroundColor',
      lightTheme.palette.white
    );
    expect(getMembershipConfigurationStyles({ theme: lightTheme }).addButtonContainer).toHaveProperty(
      'backgroundColor',
      lightTheme.palette.white
    );
    expect(getRuleCardStyles({ theme: lightTheme }).root).toEqual(
      expect.arrayContaining([expect.objectContaining({ backgroundColor: lightTheme.palette.white })])
    );
    expect(getRuleCardStyles({ theme: lightTheme }).hiddenText).toHaveProperty(
      'color',
      getAccessibleStatusColors(lightTheme).errorText
    );
  });

  it('keeps compact and regular Copilot user bubbles readable in dark and light mode', () => {
    for (const compact of [true, false]) {
      const darkColors = getUserMessageColors(darkTheme, compact);
      expect(darkColors).toEqual({
        backgroundColor: compact ? darkTheme.palette.themeDarker : darkTheme.palette.themePrimary,
        color: darkTheme.palette.neutralPrimary,
      });
      expect(contrastRatio(darkColors.color, darkColors.backgroundColor)).toBeGreaterThanOrEqual(
        NORMAL_TEXT_MIN_CONTRAST
      );

      const lightColors = getUserMessageColors(lightTheme, compact);
      expect(lightColors).toEqual({
        backgroundColor: compact ? lightTheme.palette.themeLighter : lightTheme.palette.themePrimary,
        color: compact ? lightTheme.palette.neutralPrimary : lightTheme.palette.white,
      });
      expect(contrastRatio(lightColors.color, lightColors.backgroundColor)).toBeGreaterThanOrEqual(
        NORMAL_TEXT_MIN_CONTRAST
      );
    }
  });
});
