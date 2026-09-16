import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
const id = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const conversation = {
  id,
  equipmentId: id,
  documentId: id,
  manualRevisionId: id,
  culture: 'en-US',
  createdAt: '2026-09-16',
  updatedAt: '2026-09-16',
};
for (const width of [375, 1440])
  for (const ar of [false, true]) {
    test(`Ask/history and ingest are accessible at ${width}, Arabic=${ar}`, async ({ page }) => {
      await page.route('**/api/**', (route) => {
        const path = new URL(route.request().url()).pathname;
        if (path.endsWith('/identity'))
          return route.fulfill({
            json: {
              actor: 'technician',
              role: 'Technician',
              permissions: ['read', 'start'],
              equipmentIds: [id],
            },
          });
        if (path.endsWith('/conversations')) return route.fulfill({ json: [conversation] });
        return route.fulfill({
          json: {
            conversation,
            turns: [
              {
                id,
                sequence: 1,
                question: 'pump',
                answer: 'Grounded answer',
                state: 1,
                citations: [
                  {
                    documentId: id,
                    manualRevisionId: id,
                    chunkId: id,
                    locator: 'page 2',
                    snippet: 'Original source',
                  },
                ],
              },
            ],
          },
        });
      });
      await page.setViewportSize({ width, height: 1000 });
      await page.goto('/connection');
      await page
        .getByLabel('Host bearer credential')
        .fill('test-only-credential-with-32-characters');
      await page.getByRole('button', { name: 'Use credential', exact: true }).click();
      await expect(page.getByText('Technician · technician')).toBeVisible();
      if (ar) await page.getByRole('button', { name: 'العربية', exact: true }).click();
      await page.goto('/ask');
      await page.getByRole('button', { name: new RegExp(id) }).click();
      await expect(page.getByText('Grounded answer', { exact: true })).toBeVisible();
      await page.getByText('page 2', { exact: true }).click();
      await expect(page.getByText('Original source')).toBeVisible();
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(
        true,
      );
      expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);
      await page.goto('/ingest');
      await expect(
        page.getByRole('button', { name: ar ? 'رفع وفهرسة' : 'Upload and index', exact: true }),
      ).toBeDisabled();
      expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);
      await expect(page.locator('html')).toHaveAttribute('dir', ar ? 'rtl' : 'ltr');
    });
  }
