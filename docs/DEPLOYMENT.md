# Documentation deployment

The site is built in GitHub Actions and uploaded to Cloudflare Pages. This keeps the .NET/DocFX and Node/Docusaurus toolchains in one reproducible pipeline instead of depending on a provider-specific build image.

## One-time Cloudflare setup

1. Create a Cloudflare Pages project named `nodalframework` using **Direct Upload**.
2. Create a scoped API token that can edit Cloudflare Pages for the selected account.
3. In GitHub, create a protected environment named `documentation-production`.
4. Add `CLOUDFLARE_ACCOUNT_ID` and `CLOUDFLARE_API_TOKEN` as environment secrets.
5. Add repository variable `NODAL_DOCS_DEPLOY_ENABLED` with value `true` only when automatic production deployment should begin.

Until the variable is enabled, pull requests still build and validate every documentation artifact, while pushes to `master` do not attempt an external deployment. The `Deploy Documentation` workflow can also be started manually after the environment is configured.

## Build contract

- .NET SDK comes from `global.json`.
- DocFX comes from the local tool manifest at `.config/dotnet-tools.json`.
- Node dependencies come from `website/package-lock.json`.
- The deployable directory is `website/build`.
- Wrangler is pinned in the deployment workflow.

The local production preview uses `wrangler pages dev`. The build also injects an explicit `/api/` base path into the DocFX entry page, keeping its relative styles, scripts, logo, and links correct whether a host canonicalizes `api/index.html` to `/api`, `/api/`, or an extensionless URL.

## Domain configuration

The initial canonical URL is `https://nodalframework.pages.dev`. When a custom domain is selected:

1. Attach it in Cloudflare Pages.
2. Update `url` in `website/docusaurus.config.ts`.
3. Update sitemap URLs in `website/static/robots.txt`, `llms.txt`, `llms-full.txt`, the capability graph, and DocFX configuration.
4. Run the full documentation build before merging.

The site deliberately allows `OAI-SearchBot` for discovery and citations while disallowing `GPTBot` for training. Change that policy only through an explicit project decision.
