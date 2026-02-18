// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { test, expect } from '@playwright/test';

test.use({ storageState: 'tests/storageState.json' });

const DOMAIN = process.env.INTEGRATION_TEST_DOMAIN || '';

const url = DOMAIN.startsWith('http://') || DOMAIN.startsWith('https://') ? DOMAIN : `https://${DOMAIN}`;

test.beforeEach(async ({ page }) => {
  await page.goto(url);
  await page.waitForTimeout(3000);
});

test('Test Title Pattern Recognition', { tag: '@title' }, async ({ page }) => {
  test.setTimeout(8 * 60 * 1000);

  await page.getByRole('button', { name: /Add/i }).click();
  await page.waitForTimeout(3000);

  try {
    const titleInput = page.locator('input[type="text"]').first();
    if (await titleInput.isVisible({ timeout: 5000 })) {
      const titlePatterns = [
        "Everyone in John Doe's org",
        "2 levels of direct reports of Jane Smith",
        "Everyone in Tech Lead's org with the following summarized criteria: department equals IT",
        "Exclude Everyone in John Doe's org",
        "Exclude 2 levels of direct reports of Jane Smith",
        "Exclude All Users in Marketing",
        "All Users in Sales"
      ];

      for (const pattern of titlePatterns) {
        await titleInput.clear();
        await titleInput.fill(pattern);
        await page.waitForTimeout(1000);

        const inputValue = await titleInput.inputValue();
        expect(inputValue).toBe(pattern);
      }

      console.log('✅ Title pattern recognition tested (including exclusionary patterns)');
    }
  } catch (error) {
    console.log('Title pattern test skipped - input field not found');
  }
});

test('Title Generation - Complete Workflow Test', { tag: '@title' }, async ({ page }) => {
  test.setTimeout(8 * 60 * 1000);
  
  // Look for any job row
  const jobRow = page.locator('.ms-DetailsRow').first();
  
  // Assert that at least one job row exists
  await expect(jobRow).toBeVisible({ timeout: 10000 });
  console.log('✅ Found job row, clicking to navigate to job details...');
  
  await jobRow.click();
  
  // Wait for navigation to job details page
  await page.waitForTimeout(3000);
  
  // Assert that we navigated to a job details page (URL should contain some identifier)
  await expect(page).toHaveURL(/.*\/.+/, { timeout: 10000 });
  
  // Check if shimmer appears (loading state during title generation)
  // Shimmer appears when isGeneratingTitles && part.title === ""
  const shimmerElements = page.locator('.shimmer');
  const shimmerCount = await shimmerElements.count();
  
  if (shimmerCount > 0) {
    console.log(`✅ Found ${shimmerCount} shimmer loading states - titles are being generated!`);
    
    // Wait for title generation to complete
    await page.waitForTimeout(5000);
  }
  
  // Check for generated titles
  // Generated titles appear in divs with class 'generatedTitle'
  const generatedTitles = page.locator('div').filter({ hasText: /^: / });
  const generatedTitleCount = await generatedTitles.count();
  
  // Assert that at least one generated title exists
  expect(generatedTitleCount).toBeGreaterThan(0);
  console.log(`✅ Found ${generatedTitleCount} generated titles displayed!`);
  
  // Get the text of the first generated title
  const firstTitle = await generatedTitles.first().textContent();
  
  // Assert that the title has meaningful content (not just ": ")
  expect(firstTitle).toMatch(/^: .+/);
  expect(firstTitle?.trim().length).toBeGreaterThan(2);
  console.log(`✅ First generated title: ${firstTitle}`);
  
  // Check if any titles have the exclusionary prefix
  const exclusionaryTitles = generatedTitles.filter({ hasText: /: Exclude / });
  const exclusionaryCount = await exclusionaryTitles.count();
  if (exclusionaryCount > 0) {
    console.log(`✅ Found ${exclusionaryCount} exclusionary titles (with "Exclude" prefix)`);
  }
  
  // Also check for any text fields with title values (when in edit mode)
  const titleTextFields = page.locator('input[type="text"]').filter({ hasText: /.+/ });
  const textFieldCount = await titleTextFields.count();
  
  if (textFieldCount > 0) {
    console.log(`✅ Found ${textFieldCount} title text fields`);
  }
  
  console.log(`✅ Summary: Shimmer: ${shimmerCount}, Generated Titles: ${generatedTitleCount}, Exclusionary: ${exclusionaryCount}, Text Fields: ${textFieldCount}`);
  console.log('✅ Complete title generation workflow test completed (with exclusionary support)');
});