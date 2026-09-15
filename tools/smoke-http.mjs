// Test/operator harness only. Retry only explicit pre-execution rate rejections.
export async function smokeFetch(url, options) {
  for (let attempt = 0; attempt < 3; attempt++) {
    const response = await fetch(url, { ...options, signal: AbortSignal.timeout(10000) });
    if (response.status !== 429 || attempt === 2) return response;
    const seconds = Number(response.headers.get('Retry-After'));
    if (!Number.isFinite(seconds) || seconds < 1 || seconds > 65)
      throw new Error('Invalid or excessive smoke Retry-After');
    await response.body?.cancel();
    await new Promise(resolve => setTimeout(resolve, seconds * 1000));
  }
  throw new Error('Smoke retry budget exhausted');
}
