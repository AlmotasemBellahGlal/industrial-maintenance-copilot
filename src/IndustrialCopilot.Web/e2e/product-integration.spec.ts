import { test, expect } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { randomUUID } from 'node:crypto';
test.skip(!process.env['DEMO_E2E'], 'Requires real PostgreSQL and deterministic demo API.');
for (const ar of [false, true])
  test(`Real product: upload, incremental Ask, citations, history, refusal, cancel, roles; Arabic=${ar}`, async ({
    page,
  }) => {
    test.setTimeout(90000);
    const credential = readFileSync(process.env['DEMO_CREDENTIAL_FILE'] ?? '../../artifacts/issue25/credential.txt', 'utf8').trim();
    const technician = readFileSync(
      process.env['DEMO_TECHNICIAN_FILE'] ?? '../../artifacts/issue25/technician-credential.txt',
      'utf8',
    ).trim();
    const equipment = '11111111-1111-1111-1111-111111111111',
      document = randomUUID(),
      revision = randomUUID();
    const connect = async (token: string) => {
      await page.goto('/connection');
      await page.getByRole('button', { name: 'English', exact: true }).click();
      await page.getByLabel('Host bearer credential').fill(token);
      await page.getByRole('button', { name: 'Use credential', exact: true }).click();
    };
    await connect(credential);
    await expect(page.getByText('Supervisor · demo-supervisor')).toBeVisible();
    await page.getByRole('link', { name: 'Ingest manual', exact: true }).click();
    await page.getByLabel('Equipment ID', { exact: true }).fill(equipment);
    await page.getByLabel('Document ID', { exact: true }).fill(document);
    await page.getByLabel('Revision ID', { exact: true }).fill(revision);
    await page.getByLabel('Title', { exact: true }).fill('Synthetic pump manual');
    await page
      .getByLabel('Manual file', { exact: true })
      .setInputFiles({
        name: 'pump.txt',
        mimeType: 'text/plain',
        buffer: Buffer.from(
          'SYNTHETIC TRAINING ONLY.\nPump vibration requires safe inspection.\nاهتزاز المضخة يتطلب فحصًا آمنًا.\nIsolate pump before inspection.\nSupervisor approval and verified safety remain required.',
        ),
      });
    await page.getByRole('button', { name: 'Upload and index', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Completed', exact: true })).toBeVisible();
    await page.getByRole('link', { name: 'Ask with citations', exact: true }).click();
    if (ar) await page.getByRole('button', { name: 'العربية', exact: true }).click();
    await page.getByText(ar ? 'محادثة جديدة' : 'New conversation', { exact: true }).click();
    await page.getByLabel(ar ? 'معرّف المعدة' : 'Equipment ID', { exact: true }).fill(equipment);
    await page.getByLabel(ar ? 'معرف المستند' : 'Document ID', { exact: true }).fill(document);
    await page.getByLabel(ar ? 'معرف الإصدار' : 'Revision ID', { exact: true }).fill(revision);
    const created = page.waitForResponse(
      (r) => r.url().endsWith('/api/conversations') && r.request().method() === 'POST',
    );
    await page
      .getByRole('button', { name: ar ? 'إنشاء محادثة' : 'Create conversation', exact: true })
      .click();
    const conversation = (await (await created).json()).id as string;
    await page
      .getByLabel(ar ? 'سؤالك' : 'Your question', { exact: true })
      .fill(ar ? 'اهتزاز' : 'vibration');
    await page
      .getByRole('button', {
        name: ar ? 'اسأل / أعد المحاولة بطلب جديد' : 'Ask / retry as new request',
        exact: true,
      })
      .click();
    await expect(page.getByTestId('live-answer')).not.toBeEmpty();
    await expect(
      page.getByRole('button', { name: ar ? 'إلغاء الإجابة' : 'Cancel answer', exact: true }),
    ).toBeEnabled();
    await expect(
      page.getByRole('button', { name: ar ? 'إلغاء الإجابة' : 'Cancel answer', exact: true }),
    ).toBeDisabled();
    await expect(page.locator('blockquote')).toContainText('SYNTHETIC TRAINING ONLY');
    await page.reload();
    await connect(credential);
    await page.getByRole('link', { name: 'Ask with citations', exact: true }).click();
    await page.getByRole('button', { name: new RegExp(conversation) }).click();
    await expect(
      page.getByText('SYNTHETIC TRAINING ONLY.', { exact: false }).first(),
    ).toBeVisible();
    await page.getByLabel('Your question', { exact: true }).fill('quasar astrophysics');
    await page.getByRole('button', { name: 'Ask / retry as new request', exact: true }).click();
    await expect(
      page.getByText(
        'Not enough matching evidence. Refine the question or ingest the applicable manual.',
        { exact: true },
      ),
    ).toBeVisible();
    await page.getByLabel('Your question', { exact: true }).fill('vibration');
    await page.getByRole('button', { name: 'Ask / retry as new request', exact: true }).click();
    await expect(page.getByTestId('live-answer')).not.toBeEmpty();
    await page.getByRole('button', { name: 'Cancel answer', exact: true }).click();
    await expect(page.getByRole('status').last()).toHaveText('Cancelled');
    await connect(technician);
    await expect(page.getByText('Technician · demo-technician')).toBeVisible();
    await page.getByRole('link', { name: 'Ask with citations', exact: true }).click();
    await expect(page.getByRole('button', { name: new RegExp(conversation) })).toHaveCount(0);
    await page.getByRole('link', { name: 'Ingest manual', exact: true }).click();
    await expect(
      page.getByRole('button', { name: 'Upload and index', exact: true }),
    ).toBeDisabled();
  });
