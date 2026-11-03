# Changelog

All notable changes are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and versions follow [Semantic Versioning](https://semver.org/). Before 1.0, a minor version can break the API.

## [Unreleased]

### Added

- `TemplateDsl`: templates written as plain C#, compiled by a source generator into render methods that write
  UTF-8 directly, sync or streaming, with an optional context for nested templates.
- Custom formats declared with attributes, without a generator of their own. RSS and JSON are in the samples.
- `TemplateDsl.Html`: all standard HTML elements, with typed attributes and a variant of `input` per type.
- `TemplateDsl.AspNetCore`: `ToResult()` to return a template from a minimal API endpoint or a controller action.
- Diagnostics TDSL001 to TDSL008, documented in `docs/diagnostics`.

[Unreleased]: https://github.com/aomader/TemplateDsl/commits/main
