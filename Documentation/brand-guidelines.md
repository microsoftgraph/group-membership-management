# GMM – Brand & Design Guidelines

> Sourced from: Figma file **GMM Onboarding Phase 2** (`suqUWQnWHznbX328BRsXkp`), frame **Home Page P2** (`6566:38042`), supplemented by the live codebase.

---

## 1. Design System Foundation

GMM uses **Microsoft Fluent UI v8** (`@fluentui/react`) as its design system. All colors, spacing, typography, and interactive states are driven by the Fluent UI theme object (`ITheme`). Custom overrides are minimal and listed below.

The app supports **Light** (default) and **Dark** themes, toggled at runtime via Redux state (`selectIsDarkMode`).

---

## 2. Brand Colors

### 2.1 Primary Palette

| Token | Hex | Fluent UI alias | Usage |
|---|---|---|---|
| **Brand Blue** | `#0F6CBD` | `theme.palette.themePrimary` | Navigation bar background, primary buttons, links, active states, focus rings |
| **White** | `#FFFFFF` | `theme.palette.white` | Surfaces (cards, panels), text on brand backgrounds |
| **Near White** | `#F3F2F1` | `theme.palette.neutralLighter` | Page background, table striping |
| **Divider** | `#EDEBE9` | `theme.palette.neutralQuaternaryAlt` | Horizontal rules, row separators |

### 2.2 Neutral Scale (Light Theme — Fluent UI Defaults)

| Token | Role | Approximate Hex |
|---|---|---|
| `neutralPrimary` | Body text, headings | `#323130` |
| `neutralSecondary` | Card section headers, muted labels | `#605E5C` |
| `neutralTertiary` | Subtle text (timestamps, hints) | `#A19F9D` |
| `neutralTertiaryAlt` | Inactive borders, disabled indicators | `#C8C6C4` |
| `neutralLight` | Hover backgrounds | `#EDEBE9` |
| `neutralLighter` | Page / app background | `#F3F2F1` |
| `neutralLighterAlt` | Zebra-stripe alternates | `#FAF9F8` |

### 2.3 Dark Theme Overrides

When dark mode is active, the app swaps the Fluent neutral palette using `NeutralColors` from `@fluentui/react`. The brand blue (`#0F6CBD`) remains unchanged for interactive elements.

| Token override | Value |
|---|---|
| `neutralPrimary` | `NeutralColors.white` |
| `neutralLighter` | `NeutralColors.gray180` |
| `white` | `NeutralColors.gray220` |
| `black` | `NeutralColors.gray10` |

### 2.4 Semantic / Status Colors

These map to Fluent UI semantic color tokens and are used for status badges and notifications.

| State | Token | Typical color |
|---|---|---|
| Success / Idle | `semanticColors.successIcon` | Green `#107C10` |
| Error | `semanticColors.errorText` | Red `#A4262C` |
| Warning / Threshold | `semanticColors.warningIcon` | Yellow-orange `#797673` |
| Primary action | `semanticColors.primaryButtonBackground` | `#0F6CBD` |
| Disabled | `semanticColors.disabledText` | `#A19F9D` |

### 2.5 Sync Job Status → Color Mapping

| `SyncStatus` | Meaning | Visual treatment |
|---|---|---|
| `Idle` / `InProgress` | Normal operation | Green success state |
| `ThresholdExceeded` | Awaiting owner action | Warning / orange badge |
| `CustomerPaused` / `DeveloperPaused` | Paused | Neutral gray badge |
| `PendingReview` / `PendingConfiguration` | Workflow pending | Info / blue badge |
| `DestinationGroupNotFound` / `SecurityGroupNotFound` | Error | Red error badge |
| `MembershipDataNotFound` | No source users | Red / warning badge |
| `SubmissionRejected` | Rejected | Red badge + ErrorBadgeIcon |
| `NestedGroupsFound` | Structural warning | Warning badge |
| `GuestUsersCannotBeAddedToUnifiedGroup` | Constraint error | Red badge |

---

## 3. Typography

**Primary typeface:** `Segoe UI`  
**Fallback stack (body):** `-apple-system, BlinkMacSystemFont, 'Segoe UI', 'Roboto', 'Oxygen', 'Ubuntu', 'Cantarell', 'Fira Sans', 'Droid Sans', 'Helvetica Neue', sans-serif`  
**Monospace (code / run IDs):** `Consolas, "Courier New", monospace`

### 3.1 Type Scale

| Level | Size | Weight | Line Height | Fluent token | Usage |
|---|---|---|---|---|---|
| Page Title | `24px` | `600` (Semibold) | `32px` | `theme.fonts.xxLarge` | Page headings (JobDetails, AdminConfig) |
| Section Heading | `20px` | `400–600` | `28px` | `theme.fonts.xLarge` | Sub-page section labels |
| Card Header | `16px` | `600` | `22px` | `theme.fonts.mediumPlus` | Card section titles (uppercase) |
| Nav Product Name | `16px` | `600` | `22px` | Segoe UI Semibold | App name in NavBar |
| Body Strong | `14px` | `600` | `20px` | `theme.fonts.medium` bold | Field labels, column headers |
| Body Regular | `14px` | `400` | `20px` | `theme.fonts.medium` | Cell content, descriptions |
| Caption | `12px` | `400` | `16px` | `theme.fonts.small` | Metadata, help text, run IDs |
| Micro | `10px` | `400` | `14px` | — | Timestamps, relative time |

### 3.2 Text Transform

- Card section header labels: **UPPERCASE**
- All other labels: sentence case

---

## 4. Spacing & Layout

### 4.1 Page Canvas

| Property | Value |
|---|---|
| Design canvas width | `1440px` |
| Design canvas height | `1024px` |
| Content horizontal padding | `24px` |

### 4.2 Chrome Heights

| Layer | Height | Notes |
|---|---|---|
| NavHeader | `48px` | Brand blue bar, product name + persona |
| Notification banner | `32px` | Informational strip below nav |
| Nav-Controls / Pivot | `72px` | Secondary navigation |
| Total chrome | `~176px` | Content area starts at y = 176px |

### 4.3 Data Grid Dimensions

| Element | Value |
|---|---|
| Column header row | `44px` |
| Action menu bar | `59px` |
| Data row height | `42px` |
| Row divider | `1px solid #EDEBE9` |
| Left/right content padding | `16px` |

### 4.4 Cards

| Property | Value |
|---|---|
| Padding | `18px` top/bottom, `24px` left/right |
| Border radius | `10px` |
| Background | `theme.palette.white` |
| Bottom margin | `10px` |

### 4.5 Common Spacing Units

The app follows a **4px base grid**.

| Token | px |
|---|---|
| XS | 4 |
| S | 8 |
| M | 12 |
| L | 16 |
| XL | 24 |
| XXL | 32 |

---

## 5. Components

### 5.1 Navigation Bar (NavHeader)

```
Height:          48px
Background:      #0F6CBD
Padding:         0 30px
Left content:    Microsoft waffle icon + "Membership Management" (Segoe UI Semibold 16px, white)
Right content:   Dark Mode toggle · Settings gear · Info icon · "Take a tour" pill button · Persona avatar (28px)
"Take a tour":   Outlined pill — 1px solid white, white text, Segoe UI Semibold 14px, 4px/10px padding, border-radius 20px
```

### 5.2 Persona / Avatar

- Size: `28px` × `28px`
- Border radius: `14px` (circular)
- Presence indicator: `8px` badge, bottom-right

### 5.3 Buttons

| Variant | Figma name | Usage |
|---|---|---|
| Primary | `Button` (filled) | Main CTA — brand blue fill, white text |
| Secondary | `Button/Secondary-Icon` | Secondary actions — outlined |
| Split | `Split button` | Actions with dropdown |
| Menu | `Menu button` | Overflow / kebab |
| Text/Icon | `Button/Third/Text-Icon` | Tertiary, in-table actions |

### 5.4 Status Badge (Status-Tag)

- Dimensions: approx. `62px` × `24px`
- Font: Segoe UI, `12px`, `400`
- Shape: rounded pill
- Colors: determined by `SyncStatus` / `RunHistoryStatus` (see §2.5)

### 5.5 Data Table

- Built with Fluent UI `ShimmeredDetailsList`
- First column: `Checkbox` (28px wide)
- Sortable columns use `Text-Icon-Dropdown` pattern
- Selected-row highlight: `theme.palette.themeLighter` (light blue tint)
- Row hover: `theme.palette.neutralLighter`

### 5.6 Dropdowns & Filters

- Component: `Dropdowns & Items` (Fluent UI `Dropdown`)
- Width: `320px` (standard), flexible for content
- Height: `40px`

### 5.7 Tab Navigation (Pivot)

- Located within action menu area (y = 4px inside the 59px bar)
- Active tab: underline indicator in `#0F6CBD`
- Inactive: `neutralSecondary` text
- Label: Segoe UI Regular 14px

---

## 6. Iconography

Icons come from **Fluent UI Icons** (`@fluentui/react-icons` / `initializeIcons()`).

| Icon name | Use |
|---|---|
| `Settings` | Settings navigation |
| `Info` | Information, help |
| `Weather Moon` | Dark mode toggle |
| `Sync` | Re-trigger sync job |
| `ErrorBadge` | Rejected / error status in lists |

Icon size in NavBar: `24px` × `24px`.  
Icon color on brand background: `#FFFFFF`.  
Icon color for interactive states: `theme.palette.themePrimary`.

---

## 7. Theming in Code

### Applying the theme

```typescript
import { ThemeProvider, createTheme } from '@fluentui/react';

const lightTheme = createTheme({});   // Fluent defaults — brand blue is baked in
const darkTheme  = createTheme({ palette: darkPalette });  // src/theme/palette.ts
```

### Using theme tokens in styles

```typescript
// Fluent UI style function
export const getStyles = (props: IMyStyleProps): IMyStyles => {
  const { theme } = props;
  return {
    header: {
      backgroundColor: theme.palette.themePrimary,  // #0F6CBD
      color: theme.palette.white,
    },
    bodyText: {
      color: theme.palette.neutralPrimary,
      fontFamily: 'Segoe UI',
      fontSize: 14,
      fontWeight: 400,
      lineHeight: 20,
    },
    cardTitle: {
      fontSize: 16,
      fontWeight: 600,
      lineHeight: '22px',
      textTransform: 'uppercase',
      color: theme.palette.neutralSecondary,
    }
  };
};
```

---

## 8. Accessibility Notes

- Minimum contrast ratio: **4.5:1** for body text (WCAG AA).
- White on `#0F6CBD` passes AA for normal text (contrast ≈ 4.6:1).
- Focus rings: `2px solid white` on brand backgrounds; `2px solid #0F6CBD` on light backgrounds.
- All interactive elements expose `:focus-visible` outlines.
- Icon-only buttons must have an `aria-label`.
- Use `theme.semanticColors` over raw hex values to ensure dark-mode contrast is maintained automatically.

---

## 9. Quick Reference Card

```
Brand Blue:     #0F6CBD   ← Primary CTA, nav bar, links
White:          #FFFFFF   ← Surfaces, text on blue
Near White:     #F3F2F1   ← App background
Divider:        #EDEBE9   ← Row borders, separators

Font:           Segoe UI
Sizes:          10 · 12 · 14 · 16 · 20 · 24px
Weights:        400 (Regular) · 600 (Semibold)

Nav height:     48px
Row height:     42px
Card radius:    10px
Grid unit:      4px
Canvas width:   1440px
```
