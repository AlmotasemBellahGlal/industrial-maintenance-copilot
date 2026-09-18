FROM node:26-alpine@sha256:ef24c5053d50fdc3e4e56eb4e7ddb7861874ab0fdc797046ba897581deb8e868 AS build
WORKDIR /web
COPY src/IndustrialCopilot.Web/package*.json ./
RUN npm ci --no-audit --no-fund
COPY src/IndustrialCopilot.Web/ ./
RUN npm run build
FROM nginx:1.29-alpine@sha256:5616878291a2eed594aee8db4dade5878cf7edcb475e59193904b198d9b830de AS web
COPY deploy/nginx.conf /etc/nginx/nginx.conf
COPY --from=build /web/dist/industrial-copilot-web/browser /usr/share/nginx/html
USER 101
EXPOSE 8080
ENTRYPOINT ["nginx", "-g", "daemon off;"]

FROM node:26-alpine@sha256:ef24c5053d50fdc3e4e56eb4e7ddb7861874ab0fdc797046ba897581deb8e868 AS smoke
WORKDIR /work
COPY tools/*.mjs ./tools/
RUN mkdir artifacts && chown node:node artifacts
USER node
ENTRYPOINT ["node", "tools/packaging-smoke.mjs"]
