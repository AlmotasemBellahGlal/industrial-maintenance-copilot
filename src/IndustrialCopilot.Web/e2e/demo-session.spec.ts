import { expect, test } from '@playwright/test';

const identity = {
  actor: 'visitor-test',
  role: 'Technician',
  permissions: ['read', 'start'],
  equipmentIds: ['11111111-1111-1111-1111-111111111111'],
};

for (const [language, start, ok, limited] of [
  ['en', 'Start demo session', 'Demo session started.', 'Too many demo sessions from your network.'],
  ['ar', 'ابدأ جلسة تجريبية', 'بدأت الجلسة التجريبية.', 'عدد كبير من الجلسات التجريبية من شبكتك.'],
] as const) {
  test(`demo session panel (${language}): offered, starts, keeps token in memory, reports limit`, async ({ page }) => {
    await page.addInitScript((l) => localStorage.setItem('maintenance.language', l), language);
    let mints = 0;
    let authorization = '';
    await page.route('**/api/demo/session', async (route) => {
      if (route.request().method() === 'GET')
        return route.fulfill({ json: { enabled: true, lifetimeSeconds: 7200 } });
      mints++;
      if (mints > 1) return route.fulfill({ status: 429, json: { error: 'session_limit' } });
      return route.fulfill({
        status: 201,
        json: { token: 'dms_' + 'A'.repeat(43), expiresAt: new Date(Date.now() + 7200000).toISOString() },
      });
    });
    await page.route('**/api/identity', async (route) => {
      authorization = route.request().headers()['authorization'] ?? '';
      return route.fulfill({ json: identity });
    });
    await page.goto('/connection');
    await page.getByRole('button', { name: start, exact: true }).click();
    await expect(page.getByRole('status').first()).toContainText(ok);
    await expect.poll(() => authorization).toBe('Bearer dms_' + 'A'.repeat(43));
    expect(await page.evaluate(() => JSON.stringify({ ...localStorage, ...sessionStorage }))).not.toContain('dms_');
    await page.getByRole('button', { name: start, exact: true }).click();
    await expect(page.getByRole('status').first()).toContainText(limited);
  });
}

test('demo panel is absent when the host does not offer sessions', async ({ page }) => {
  await page.route('**/api/demo/session', (route) => route.fulfill({ status: 404, json: { error: 'not_found' } }));
  await page.goto('/connection');
  await expect(page.getByRole('heading', { name: 'Session credential' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Start demo session' })).toHaveCount(0);
});
