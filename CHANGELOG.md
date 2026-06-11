# Changelog

## 0.2.0

- Migrated Atlas Platform integration to **API v0.2**: workspace-scoped upload/download URLs, `Authorization: Bearer` workspace API keys, and multipart file upload.
- Added **Authentication** settings (**Workspace API Key**, `ATLAS_API_KEY` / `API_KEY` environment fallback) with pre-flight validation before workflow runs.
- Fixed HTTP request content disposal on file upload (prevented null-reference failures after successful upload).
- Fixed Running Jobs elapsed timer and progress animation stability in Job History.
- Updated `README.md` for API v0.2 setup and troubleshooting (aligned with Atlas Unreal plugin docs).

## 0.1.0

- Initial editor release: single workflow runs, batch editor, job history, async polling API, and GLB/texture/audio output support.
