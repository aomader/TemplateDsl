# Changelog

All notable changes are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and versions follow [Semantic Versioning](https://semver.org/). Before 1.0, a minor version can break the API.

## [Unreleased]

### Added

- Templates can be structs and records, such as a `readonly record struct` for a row, which a parent renders without
  allocating it, as do `RenderTo`, `RenderToString` and `RenderToAsync`, which take a template as its own type.
- Children given to a template in brackets are written without allocating: they become a method of the template
  that gives them, instead of a lambda.
- `IHtmlContent` and `IHtmlContent<TContext>` for content passed to a layout or a slot, without or with a context: any HTML template that renders with that context,
  because it needs it, less of it, or none. Brackets take it, and templates of any type: an interface such as
  `IHtmlTemplate`, or a type parameter, which renders a struct without boxing it. The context is checked where content is
  passed and where it is placed. `H.Each` takes a lambda that returns such content too.
- `IAsyncHtmlContent` and `IAsyncHtmlContent<TContext>` for content of an async template, such as a layout that may host a sync or an async page:
  any HTML template that renders with that context, placed with `await content`. A sync template can be awaited too,
  and renders in one go.
- TDSL009 reports a template converted to a node outside of `Build`, which is only how `Build` places it.
- `RenderTo(writer)` also through an interface, such as `IHtmlTemplate`.

### Changed

- **Breaking:** templates implement an interface instead of deriving from a base class: `IHtmlTemplate`,
  `IHtmlTemplate<TContext>`, `IAsyncHtmlTemplate` and `IAsyncHtmlTemplate<TContext>`, with `IAcceptsChildren` to take
  children. `Build` is no longer an override, except in a class derived from another template.
  `HtmlTemplate`, `HtmlTemplateWithChildren`, the async variants and `[TemplateBase]` are gone. A custom format
  declares an interface such as `public interface IXmlTemplate : ITemplate<XmlNode>;`.
- Templates don't have to be sealed. One derived from another can override `Build` or the members it uses, and an
  abstract one without `Build` gets it, and its render methods, as abstract.
- A template with an error gets its generated members anyway, so the reason is the only error, instead of errors about
  `RenderTo` not being implemented. An IDE only suggests implementing `Build`.
- TDSL002 now reports a `ref struct`, instead of a template that isn't sealed.
- `RenderTo`, `RenderToString` and `RenderToAsync` take a template or content, such as `IHtmlTemplate` or
  `IAsyncHtmlContent`, and no longer a bare `ITemplateRenderable` or `ISyncRenderable`, which only say what kind
  of template something is. `IStreamingRenderable` is gone.
- Brackets take `TemplateChildren<TNode>`, so a child that is neither a node nor a template is now reported by C# as a
  type that can't be used for `TTemplate` (CS0311 or CS0315), instead of as a missing conversion.

## [0.1.0-preview.2] - 2026-11-05

### Added

- Missing CONTRIBUTING.md and SECURITY.md docs

### Changed

- Restructured the README

## [0.1.0-preview.1] - 2026-11-04

### Added

- `TemplateDsl`: templates written as plain C#, compiled by a source generator into render methods that write
  UTF-8 directly, sync or streaming, with an optional context for nested templates.
- Custom formats declared with attributes, without a generator of their own. RSS and JSON are in the samples.
- `TemplateDsl.Html`: all standard HTML elements, with typed attributes and a variant of `input` per type.
- `TemplateDsl.AspNetCore`: `ToResult()` to return a template from a minimal API endpoint or a controller action.
- Diagnostics TDSL001 to TDSL008, documented in `docs/diagnostics`.

[Unreleased]: https://github.com/aomader/TemplateDsl/compare/0.1.0-preview.2...HEAD
[0.1.0-preview.2]: https://github.com/aomader/TemplateDsl/releases/tag/0.1.0-preview.2
[0.1.0-preview.1]: https://github.com/aomader/TemplateDsl/releases/tag/0.1.0-preview.1