import { test, expect } from '@playwright/test';
import { readFileSync } from 'node:fs';

test.skip(
  !process.env['DEMO_E2E'],
  'Requires the separate loopback demo host and real PostgreSQL.',
);
test('Real API/PG: diagnosis SSE → bilingual review → approval → safety block → verification → dispatch → trace', async ({
  page,
}) => {
  const credential = readFileSync(process.env['DEMO_CREDENTIAL_FILE'] ?? '../../artifacts/issue25/credential.txt', 'utf8').trim();
  const api = async (path: string, body?: unknown, culture = 'en-US') =>
    page.request.fetch((process.env['DEMO_API_BASE'] ?? 'http://127.0.0.1:5000/api') + path, {
      method: body === undefined ? 'GET' : 'POST',
      headers: { Authorization: `Bearer ${credential}`, 'Accept-Language': culture },
      data: body,
    });
  await page.goto('/connection');
  await page.getByLabel('Host bearer credential').fill(credential);
  await page.getByRole('button', { name: 'Use credential', exact: true }).click();
  await page.getByRole('link', { name: 'New diagnosis', exact: true }).click();
  await page
    .getByLabel('Equipment ID', { exact: true })
    .fill('11111111-1111-1111-1111-111111111111');
  await page
    .getByLabel('Reported symptom', { exact: true })
    .fill('pump vibration and seal leakage');
  const stream = page.waitForResponse((r) => r.url().endsWith('/api/runs/stream'));
  await page.getByRole('button', { name: 'Start grounded diagnosis', exact: true }).click();
  const response = await stream;
  expect(response.headers()['content-type']).toContain('text/event-stream');
  expect(response.request().headers()['accept-language']).toBe('en-US');
  await expect(
    page.getByText('The manual supports checking pump vibration and seal leakage.', {
      exact: false,
    }),
  ).toBeVisible();
  await expect(page.locator('.timeline')).toContainText('Waiting For Approval');
  const traceId = (await page
    .getByRole('link', { name: 'Inspect safe trace', exact: true })
    .getAttribute('href'))!
    .split('/')
    .pop()!;
  await page.getByRole('link', { name: 'Review proposed work order', exact: true }).click();
  await expect(page).toHaveURL(/\/work-orders\/[a-f0-9-]{36}$/);
  await expect(page.getByRole('heading', { name: 'Work order review', exact: true })).toBeVisible();
  const id = page.url().split('/').pop()!;
  const initial = await (await api(`/work-orders/${id}`)).json();
  expect((await api(`/work-orders/${id}/dispatch`, initial.target)).status()).toBe(422);
  const evidence = await (await api(`/work-orders/${id}/evidence`)).json();
  expect(evidence.evidence[0].snippet).toContain('SYNTHETIC TRAINING MANUAL');
  await page.getByRole('button', { name: 'العربية', exact: true }).click();
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await expect(page.getByRole('heading', { name: 'مراجعة أمر العمل', exact: true })).toBeVisible();
  await expect(page.locator('blockquote')).toContainText(evidence.evidence[0].snippet);
  expect(
    await page
      .locator('.identifier')
      .first()
      .evaluate((e) => getComputedStyle(e).direction),
  ).toBe('ltr');
  await page.getByRole('button', { name: 'English', exact: true }).click();
  await page.getByRole('button', { name: /^Approve revision/ }).click();
  await page.getByRole('button', { name: 'Confirm approve', exact: true }).click();
  await expect(page.locator('.review-strip')).toContainText('Approved');
  expect(
    (
      await api(
        `/work-orders/${id}/decisions`,
        { target: initial.target, decision: 'Approve' },
        'ar-EG',
      )
    ).status(),
  ).toBe(409);
  await page.getByRole('button', { name: 'Review dispatch request', exact: true }).click();
  await page.getByRole('button', { name: 'Request dispatch', exact: true }).click();
  await expect(page.getByRole('alert').first()).toContainText('server did not accept');
  await page.getByRole('button', { name: 'Reload current review', exact: true }).click();
  await page.getByText('Record a human safety check', { exact: true }).click();
  await page
    .getByLabel('Prerequisite', { exact: true })
    .selectOption('44444444-4444-4444-4444-444444444444');
  await page
    .getByLabel('Physical verification evidence', { exact: true })
    .fill('SIMULATED demo: isolated and observed zero energy.');
  await page.getByLabel('I verified this prerequisite is satisfied', { exact: true }).check();
  await page.getByRole('button', { name: 'Review verification', exact: true }).click();
  await page.getByRole('button', { name: 'Record verification', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('Verification saved');
  await page.getByRole('button', { name: 'Review dispatch request', exact: true }).click();
  await page.getByRole('button', { name: 'Request dispatch', exact: true }).click();
  await expect(page.locator('.review-strip')).toContainText('Dispatched');
  const trace = await (await api(`/traces/${traceId}`)).json();
  expect(trace.steps.filter((s: { kind: string }) => s.kind === 'Agent')).toHaveLength(3);
  expect(trace.steps.some((s: { name: string }) => s.name === 'deterministic_safety_policy')).toBe(
    true,
  );
  const ar = await api(
    '/runs',
    { equipmentId: initial.content.equipmentId, symptom: 'pump vibration' },
    'ar-EG',
  );
  const arabicRun = await ar.json();
  expect(arabicRun.outcome).toBe('Proposed');
  expect(arabicRun.narrative).toMatch(/[\u0600-\u06ff]/);
  const arabicReview = await (await api(`/work-orders/${arabicRun.workOrderId}`)).json();
  expect(arabicReview.requirements).toEqual(initial.requirements);
  const arEvidence = await (await api(`/work-orders/${arabicRun.workOrderId}/evidence`)).json();
  expect(arEvidence.evidence).toEqual(evidence.evidence);
});
