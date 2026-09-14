# IndustrialCopilot.Web

Standalone Angular 22 operations workspace. See [frontend setup and demo](../../docs/FRONTEND-SETUP.md) and the [design-system master](../../design-system/industrial-maintenance-copilot/MASTER.md).

Requires Node 26/npm 11. Run npm ci, npm start, npm test, npm run typecheck, npm run build, and npm run test:e2e from this directory.

Configure the backend separately; development proxy defaults to loopback port 5000. Production uses same-origin HTTPS proxying. Never put bearer credentials in source or build configuration.
