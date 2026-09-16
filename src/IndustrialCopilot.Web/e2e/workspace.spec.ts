import { test, expect, Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
const runtimeErrors = new WeakMap<Page, string[]>();
test.beforeEach(({ page }) => {
  const errors: string[] = [];
  runtimeErrors.set(page, errors);
  page.on('pageerror', (error) => errors.push(error.message));
  page.on('console', (message) => {
    if (message.type() === 'error' && message.text().startsWith('ERROR'))
      errors.push(message.text());
  });
});
test.afterEach(({ page }) => expect(runtimeErrors.get(page)).toEqual([]));
const orderId = '11111111-1111-1111-1111-111111111111',
  manual = '22222222-2222-2222-2222-222222222222',
  revision = '33333333-3333-3333-3333-333333333333',
  requirement = '44444444-4444-4444-4444-444444444444';
function review() {
  return {
    workOrderId: orderId,
    target: { revision: 2, concurrencyToken: 'version-2' },
    content: {
      equipmentId: orderId,
      manualId: manual,
      manualRevisionId: revision,
      reportedSymptom: 'Noise at the bearing housing under normal load.',
      description: 'Inspect pump bearing',
      actions: [{ order: 1, instruction: 'Inspect bearing housing after isolation.' }],
    },
    status: 'PendingApproval',
    safetyAssessmentRevision: 2,
    requirements: [
      {
        id: requirement,
        description: 'Isolate and verify zero energy',
        mandatory: true,
        status: 'Unverified',
        verifiedBy: null as string | null,
        evidence: null as string | null,
      },
    ],
    decision: null as string | null,
  };
}
async function credential(page: Page) {
  await page.goto('/connection');
  await page.getByLabel('Host bearer credential').fill('isolated-browser-test-only-credential');
  await page.getByRole('button', { name: 'Use credential', exact: true }).click();
  await expect(page.getByRole('status').first()).toContainText('Credential configured');
}
async function openReview(page: Page) {
  await credential(page);
  if (!(await page.getByRole('link', { name: 'Work orders', exact: true }).isVisible()))
    await page.getByRole('button', { name: 'Open navigation', exact: true }).click();
  await page.getByRole('link', { name: 'Work orders', exact: true }).click();
  await page.getByLabel('Work orders ID').fill(orderId);
  await page.getByRole('button', { name: 'Open record', exact: true }).click();
}
async function backend(page: Page) {
  let current = review();
  const requests: { path: string; body: Record<string, unknown> }[] = [];
  await page.route('**/api/**', async (route) => {
    const req = route.request(),
      path = new URL(req.url()).pathname;
    const body = req.method() === 'POST' ? (req.postDataJSON() as Record<string, unknown>) : {};
    if (req.method() === 'POST') requests.push({ path, body });
    if (path === '/api/identity')
      return route.fulfill({
        json: {
          actor: 'test-supervisor',
          role: 'Supervisor',
          permissions: ['read', 'start', 'approve', 'verify', 'dispatch', 'ingest'],
          equipmentIds: [orderId],
        },
      });
    if (path.endsWith('/evidence'))
      return route.fulfill({
        json: {
          workOrderId: orderId,
          currentRevision: current.target.revision,
          source: 'original_proposal',
          evidence: [
            {
              documentId: manual,
              manualRevisionId: revision,
              chunkId: requirement,
              locator: 'Section 4.2 / page 18',
              snippet:
                '<img src=x onerror=alert(1)> Inspect only after the verified zero-energy state.',
            },
          ],
        },
      });
    if (path.endsWith('/edited-safety-preview'))
      return route.fulfill({
        json: {
          canProceed: true,
          requirements: [
            { id: requirement, description: 'Isolate and verify zero energy', mandatory: true },
          ],
        },
      });
    if (path.endsWith('/decisions')) {
      current = {
        ...current,
        status: body['decision'] === 'Reject' ? 'Rejected' : 'Approved',
        decision: String(body['decision']),
        target: {
          revision: body['decision'] === 'EditAndApprove' ? 3 : 2,
          concurrencyToken: 'new-version',
        },
      };
      if (body['editedContent']) current.content = body['editedContent'] as typeof current.content;
      return route.fulfill({ json: { outcome: 'Applied', review: current } });
    }
    if (path.endsWith('/verifications')) {
      current.requirements[0] = {
        ...current.requirements[0],
        status: 'Satisfied',
        verifiedBy: 'trusted-technician',
        evidence: String(body['evidence']),
      };
      current.target.concurrencyToken = 'verified-version';
      return route.fulfill({ json: { outcome: 'Ready', concurrencyToken: 'verified-version' } });
    }
    if (path.endsWith('/dispatch'))
      return route.fulfill({
        status: 202,
        json: {
          attemptId: requirement,
          workOrderId: orderId,
          revision: current.target.revision,
          outcome: 'Ready',
          state: 'Uncertain',
          externalReference: null,
          failure: null,
        },
      });
    if (path.includes('/dispatch-attempts/'))
      return route.fulfill({
        json: {
          attemptId: requirement,
          workOrderId: orderId,
          revision: current.target.revision,
          outcome: 'Ready',
          state: 'Uncertain',
          externalReference: null,
          failure: null,
        },
      });
    if (path.includes('/work-orders/')) return route.fulfill({ json: current });
    if (path.includes('/runs/'))
      return route.fulfill({
        json: {
          runId: orderId,
          equipmentId: orderId,
          symptom: 'noise',
          status: 'WaitingForApproval',
          cancellationRequested: false,
          workOrderIds: [orderId],
          executionIds: [revision],
        },
      });
    if (path.includes('/traces/'))
      return route.fulfill({
        json: {
          executionId: revision,
          correlationId: manual,
          runId: orderId,
          steps: [
            {
              kind: 'Agent',
              name: 'SymptomMatcher',
              status: 'Succeeded',
              error: null,
              startedAt: '2026-09-13T12:00:00Z',
              completedAt: '2026-09-13T12:00:01Z',
            },
          ],
        },
      });
    return route.fulfill({ status: 404, json: { error: 'not_found' } });
  });
  return requests;
}
test('review, literal evidence, approval confirmation, verification and uncertain dispatch remain distinct', async ({
  page,
}) => {
  const requests = await backend(page);
  await credential(page);
  if (!(await page.getByRole('link', { name: 'Work orders', exact: true }).isVisible()))
    await page.getByRole('button', { name: 'Open navigation', exact: true }).click();
  await page.getByRole('link', { name: 'Work orders', exact: true }).click();
  await page.getByLabel('Work orders ID').fill(orderId);
  await page.getByRole('button', { name: 'Open record', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Grounded evidence', exact: true })).toBeVisible();
  await expect(page.locator('blockquote')).toContainText('<img src=x');
  await expect(page.locator('blockquote img')).toHaveCount(0);
  await page.getByRole('button', { name: 'Approve revision 2', exact: true }).click();
  await expect(page.getByRole('dialog')).toBeVisible();
  expect(requests).toHaveLength(0);
  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog')).not.toBeVisible();
  await page.getByRole('button', { name: 'Approve revision 2', exact: true }).click();
  await page.getByRole('button', { name: 'Confirm approve', exact: true }).click();
  await expect(page.locator('.review-strip')).toContainText('Approved');
  expect(requests[0].body).toEqual({
    target: { revision: 2, concurrencyToken: 'version-2' },
    decision: 'Approve',
  });
  await page.getByText('Record a human safety check', { exact: true }).click();
  await page.getByLabel('Prerequisite', { exact: true }).selectOption(requirement);
  await page.getByLabel('Physical verification evidence').fill('Meter confirms zero energy');
  await page.getByLabel('I verified this prerequisite is satisfied').check();
  await page.getByRole('button', { name: 'Review verification', exact: true }).click();
  await page.getByRole('button', { name: 'Record verification', exact: true }).click();
  await expect(page.getByText('Verified by trusted-technician', { exact: false })).toBeVisible();
  await page.getByRole('button', { name: 'Review dispatch request', exact: true }).click();
  await page.getByRole('button', { name: 'Request dispatch', exact: true }).click();
  await expect(
    page.getByRole('button', { name: 'Review dispatch request', exact: true }),
  ).toBeDisabled();
  await page.getByRole('link', { name: 'Inspect dispatch attempt ' + requirement }).click();
  await expect(
    page.getByText('This is neither success nor failure.', { exact: false }),
  ).toBeVisible();
  await page.getByRole('button', { name: 'Refresh dispatch status' }).click();
  expect(requests.filter((r) => r.path.endsWith('/dispatch'))).toHaveLength(1);
});
test('browser back dismisses confirmation without submitting a decision', async ({ page }) => {
  const requests = await backend(page);
  await credential(page);
  if (!(await page.getByRole('link', { name: 'Work orders', exact: true }).isVisible()))
    await page.getByRole('button', { name: 'Open navigation', exact: true }).click();
  await page.getByRole('link', { name: 'Work orders', exact: true }).click();
  await page.getByLabel('Work orders ID').fill(orderId);
  await page.getByRole('button', { name: 'Open record', exact: true }).click();
  await page.getByRole('button', { name: 'Approve revision 2', exact: true }).click();
  await expect(page.getByRole('dialog')).toBeVisible();
  await page.goBack();
  await expect(page.getByRole('heading', { name: 'Work orders', exact: true })).toBeVisible();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  expect(requests).toHaveLength(0);
});
test('edited scope preview is invalidated on change and exact preview is echoed at approval', async ({
  page,
}) => {
  const requests = await backend(page);
  await openReview(page);
  await page.getByRole('button', { name: 'Edit & approve', exact: true }).click();
  await page.getByLabel('Work order description', { exact: true }).fill('Final reviewed repair');
  await page.getByRole('button', { name: 'Assess edited scope', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Review final edit & approval' })).toBeVisible();
  await page.getByLabel('Action 1', { exact: true }).fill('Different exact instruction');
  await expect(page.getByRole('button', { name: 'Review final edit & approval' })).toHaveCount(0);
  await page.getByRole('button', { name: 'Assess edited scope', exact: true }).click();
  await page.getByRole('button', { name: 'Review final edit & approval' }).click();
  await page.getByRole('button', { name: 'Confirm edit & approval' }).click();
  await expect(page.locator('.review-strip')).toContainText('Revision 3');
  const decision = requests.find((r) => r.path.endsWith('/decisions'))!;
  expect(decision.body['reviewedRequirements']).toEqual([
    { id: requirement, description: 'Isolate and verify zero energy', mandatory: true },
  ]);
  expect(decision.body['editedContent']).toHaveProperty('description', 'Final reviewed repair');
  expect(decision.body).not.toHaveProperty('actorId');
});
test('stale decision pauses actions and offers reload rather than overwrite', async ({ page }) => {
  await backend(page);
  await page.route('**/decisions', (r) =>
    r.fulfill({ status: 409, json: { outcome: 'Conflict' } }),
  );
  await openReview(page);
  await page.getByRole('button', { name: 'Approve revision 2', exact: true }).click();
  await page.getByRole('button', { name: 'Confirm approve' }).click();
  await expect(page.getByRole('alert').first()).toContainText('Review changed');
  await expect(
    page.getByRole('button', { name: 'Approve revision 2', exact: true }),
  ).toBeDisabled();
  await page.getByRole('button', { name: 'Reload current review' }).click();
  await expect(page.getByRole('button', { name: 'Approve revision 2', exact: true })).toBeEnabled();
});
test('reject requires contextual confirmation and becomes terminal', async ({ page }) => {
  await backend(page);
  await openReview(page);
  await page.getByRole('button', { name: 'Reject', exact: true }).click();
  await expect(page.getByRole('dialog')).toContainText('revision 2');
  await page.getByRole('button', { name: 'Confirm reject' }).click();
  await expect(page.locator('.review-strip')).toContainText('Rejected');
  await expect(page.getByRole('button', { name: 'Review dispatch request' })).toHaveCount(0);
});
for (const status of [401, 403])
  test(`distinct ${status} access error without raw internals`, async ({ page }) => {
    await page.route('**/api/**', (r) =>
      r.fulfill({ status, json: { error: 'secret SQL stack' } }),
    );
    await openReview(page);
    await expect(page.getByRole('alert')).toContainText(
      status === 401 ? 'Authentication required' : 'Permission denied',
    );
    await expect(page.locator('main')).not.toContainText('secret SQL stack');
  });
for (const outcome of ['Proposed', 'Blocked', 'Failed'])
  test(`SSE ${outcome} consumes actual envelopes, deduplicates and sends authenticated POST once`, async ({
    page,
  }) => {
    let calls = 0;
    const frame = {
      CorrelationId: manual,
      ExecutionId: revision,
      progress: { Kind: 0, RunId: orderId, Role: null, Allowed: null, WorkOrderId: null },
    };
    await page.route('**/api/runs/stream', async (r) => {
      calls++;
      expect(r.request().method()).toBe('POST');
      expect(r.request().headers()['authorization']).toContain('isolated-browser-test-only');
      expect(r.request().postDataJSON()).toEqual({
        equipmentId: orderId,
        symptom: 'Bearing noise',
      });
      await r.fulfill({
        contentType: 'text/event-stream',
        body: `event: WorkflowStarted\ndata: ${JSON.stringify(frame)}\n\nevent: WorkflowStarted\ndata: ${JSON.stringify(frame)}\n\nevent: Result\ndata: ${JSON.stringify({ RunId: orderId, ExecutionId: revision, CorrelationId: manual, WorkOrderId: outcome === 'Proposed' ? orderId : null, Outcome: outcome })}\n\n`,
      });
    });
    await credential(page);
    await page.getByRole('link', { name: 'New diagnosis', exact: true }).click();
    await page.getByLabel('Equipment ID').fill(orderId);
    await page.getByLabel('Reported symptom').fill('Bearing noise');
    await page.getByRole('button', { name: 'Start grounded diagnosis' }).click();
    await expect(page.locator('.timeline li')).toHaveCount(1);
    await expect(page.getByRole('link', { name: 'Inspect run', exact: true })).toBeVisible();
    expect(calls).toBe(1);
    await expect(page.getByRole('link', { name: 'Review proposed work order' })).toHaveCount(
      outcome === 'Proposed' ? 1 : 0,
    );
  });
test('forms reject invalid identity and blank symptom before network', async ({ page }) => {
  await credential(page);
  await page.getByRole('link', { name: 'New diagnosis', exact: true }).click();
  await page.getByLabel('Equipment ID').fill('not-an-id');
  await page.getByLabel('Reported symptom').fill('   ');
  await page.getByRole('button', { name: 'Start grounded diagnosis' }).click();
  await expect(page.getByText('Enter a nonempty equipment UUID.')).toBeVisible();
  await expect(page.getByLabel('Equipment ID')).toBeFocused();
});
for (const width of [375, 768, 1024, 1440])
  test(`responsive and accessibility ${width}px with long evidence`, async ({ page }) => {
    await page.setViewportSize({ width, height: 1000 });
    await backend(page);
    await page.goto('/');
    await expect(page.getByRole('heading', { name: 'Maintenance, with evidence.' })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(
      true,
    );
    let results = await new AxeBuilder({ page })
      .withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa'])
      .analyze();
    expect(results.violations).toEqual([]);
    await openReview(page);
    await expect(page.locator('blockquote')).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(
      true,
    );
    results = await new AxeBuilder({ page })
      .withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa'])
      .analyze();
    expect(results.violations).toEqual([]);
    await page.screenshot({ path: `test-results/review-${width}.png`, fullPage: true });
    await page.getByRole('button', { name: 'العربية', exact: true }).click();
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await expect(
      page.getByRole('heading', { name: 'مراجعة أمر العمل', exact: true }),
    ).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(
      true,
    );
    expect(
      (
        await new AxeBuilder({ page })
          .withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa'])
          .analyze()
      ).violations,
    ).toEqual([]);
    await page.screenshot({ path: `test-results/review-ar-${width}.png`, fullPage: true });
  });

for (const state of ['Pending', 'Confirmed', 'DefinitivelyFailed']) {
  test(`dispatch inspection preserves ${state} without a new delivery`, async ({ page }) => {
    let posts = 0;
    await page.route('**/api/**', (route) => {
      if (route.request().method() === 'POST') posts++;
      return route.fulfill({
        json: {
          attemptId: requirement,
          workOrderId: orderId,
          revision: 2,
          outcome: 'Ready',
          state,
          externalReference: state === 'Confirmed' ? 'ticket-17' : null,
          failure: null,
        },
      });
    });
    await page.goto('/dispatch/' + requirement);
    await expect(page.locator('app-status').first()).toHaveText(state);
    await page.getByRole('button', { name: 'Refresh dispatch status' }).click();
    expect(posts).toBe(0);
  });
}

test('Arabic approval dialog keeps keyboard cancellation and source scope intact', async ({
  page,
}) => {
  await backend(page);
  await openReview(page);
  await page.getByRole('button', { name: 'العربية', exact: true }).click();
  const approve = page.getByRole('button', { name: /^الموافقة على الإصدار/ });
  await approve.click();
  await expect(page.getByRole('dialog')).toBeVisible();
  await expect(page.getByRole('dialog')).toContainText('Inspect pump bearing');
  await expect(page.getByRole('button', { name: 'رجوع', exact: true })).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog')).not.toBeVisible();
  await expect(approve).toBeFocused();
});
test('run inspection links to work orders and safe execution traces', async ({ page }) => {
  await backend(page);
  await page.goto('/runs');
  await page.getByLabel('Maintenance runs ID').fill(orderId);
  await page.getByRole('button', { name: 'Open record', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Maintenance run', exact: true })).toBeVisible();
  await page.getByRole('link', { name: revision, exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Execution activity' })).toBeVisible();
  await expect(page.locator('.timeline')).toContainText('SymptomMatcher');
});
test('reduced-motion mobile navigation and confirmation retain accessible focus', async ({
  page,
}) => {
  await page.setViewportSize({ width: 375, height: 812 });
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await backend(page);
  await openReview(page);
  const nav = page.getByRole('button', { name: 'Open navigation' });
  await nav.click();
  await expect(page.getByRole('navigation', { name: 'Primary' })).toBeVisible();
  await page.getByRole('button', { name: 'Close navigation' }).click();
  await page.getByRole('button', { name: 'Approve revision 2', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Go back', exact: true })).toBeFocused();
  const audit = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa'])
    .analyze();
  expect(audit.violations).toEqual([]);
  await page.keyboard.press('Escape');
  await expect(page.getByRole('button', { name: 'Approve revision 2', exact: true })).toBeFocused();
});

test('degraded result preserves exact evidence and has no review authority in either language', async ({
  page,
}) => {
  await backend(page);
  await page.route('**/api/runs/stream', async (route) =>
    route.fulfill({
      contentType: 'text/event-stream',
      body: `event: Result\ndata: ${JSON.stringify({ RunId: orderId, ExecutionId: revision, CorrelationId: manual, WorkOrderId: null, Outcome: 'Degraded', Narrative: 'Advisory evidence answer', DegradationReason: 'transient_exhausted', Citations: [{ DocumentId: manual, ManualRevisionId: revision, ChunkId: requirement, Locator: 'page 7', Snippet: 'Exact source — do not translate.' }] })}\n\n`,
    }),
  );
  await credential(page);
  await page.getByRole('link', { name: 'New diagnosis', exact: true }).click();
  await page.getByLabel('Equipment ID').fill(orderId);
  await page.getByLabel('Reported symptom').fill('Bearing noise');
  await page.getByRole('button', { name: 'Start grounded diagnosis' }).click();
  await expect(
    page.getByText('Grounded fallback — no work order created', { exact: true }),
  ).toBeVisible();
  await page.locator('summary').filter({ hasText: 'page 7' }).click();
  await expect(page.getByText('Exact source — do not translate.', { exact: true })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Review proposed work order' })).toHaveCount(0);
  await page.getByRole('button', { name: 'العربية', exact: true }).click();
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await expect(
    page.getByText('إجابة بديلة مستندة إلى الأدلة — لم يُنشأ أمر عمل', { exact: true }),
  ).toBeVisible();
  await expect(page.getByText('Exact source — do not translate.', { exact: true })).toBeVisible();
  expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);
});
