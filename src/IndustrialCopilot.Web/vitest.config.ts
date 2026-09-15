import { defineConfig } from 'vitest/config';

// A bounded worker pool keeps local full-system validation practical on development machines.
export default defineConfig({ test: { maxWorkers: 1, fileParallelism: false } });
