// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { DefaultPalette, NeutralColors, type IPalette } from '@fluentui/react';

export const darkPalette: Partial<IPalette> = {
  ...DefaultPalette,
  neutralLighterAlt: NeutralColors.gray200,
  neutralLighter: NeutralColors.gray180,
  neutralLight: NeutralColors.gray160,
  neutralQuaternaryAlt: NeutralColors.gray150,
  neutralQuaternary: NeutralColors.gray140,
  neutralTertiaryAlt: NeutralColors.gray120,
  neutralTertiary: NeutralColors.gray60,
  neutralSecondary: NeutralColors.gray50,
  neutralSecondaryAlt: NeutralColors.gray50,
  neutralPrimaryAlt: NeutralColors.gray40,
  neutralPrimary: NeutralColors.white,
  neutralDark: NeutralColors.gray20,
  black: NeutralColors.gray10,
  white: NeutralColors.gray220,
};