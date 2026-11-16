# Changelog

All notable changes are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and versions follow [Semantic Versioning](https://semver.org/). Before 1.0, a minor version can break the API.

## [Unreleased]

## [0.1.0] - 2026-11-16

The first release. Templates are plain C#, which a source generator compiles into code that writes UTF-8 directly,
so rendering builds no tree, and an error in a template is a compile error.

### Added

- **TemplateDsl**, the core:
  - Templates implement an interface, such as `IHtmlTemplate`, and describe their output in `Build`, with loops,
    conditions, switches and nested templates, sync, async or streaming, and with a context for nested templates if
    they need one.
  - Templates can be classes, structs or records, and take children in brackets, without allocating. `IHtmlContent`
    and `IAsyncHtmlContent` pass content to a layout or a slot, checked for the context it needs.
  - Templates are created without `new`: the generator writes a factory for each into the class `Templates`, so with
    `using static App.Templates;` a template is written `Card("Hello") [ ... ]`.
  - Supports trimming, native AOT and Hot Reload.
- **TemplateDsl.Html**:
  - All standard HTML elements in `Elements`, written without a prefix with `using static TemplateDsl.Html.Elements;`,
    and a variant of `input` per type, such as `InputEmail`.
  - Typed attributes, such as `.ColSpan(2)`, `.Draggable(true)` and dates, and `.Attr(name, value)` for any other.
  - The ARIA attributes of WAI-ARIA 1.2 with the types it gives them, such as `.AriaExpanded(isOpen)` and
    `.Role(Role.Alert)`, plus `.Aria(name, value)` and `.Data(name, value)` for the others.
  - Lists of names with conditions for `class`, `rel`, `sandbox` and the ARIA lists of IDs:
    `Li.Class("todo", ("done", todo.Done))`.
  - `Doctype`, and `Comment(text)`, whose text can't end or break the comment.
- **TemplateDsl.AspNetCore**: `ToResult()` returns a template from a minimal API endpoint or a controller action.
- **Custom formats**, declared with attributes and compiled by the same generator, as the RSS and JSON samples show.
  Packages can add attributes to the elements of a format with extension methods, such as for htmx.
- Diagnostics TDSL001 to TDSL012, documented in `docs/diagnostics`.

### Changed

For those coming from the previews, these break code written for 0.1.0-preview.8:

- **Breaking:** the HTML elements are in `Elements` instead of `H`. `using H = TemplateDsl.Html.Elements;`, or
  `<Using Include="TemplateDsl.Html.Elements" Alias="H" />` for the whole project, keeps code written for `H` compiling.
- **Breaking:** the node classes, such as `Div`, are in `TemplateDsl.Html.Nodes` instead of `TemplateDsl.Html.Elements`.
- **Breaking:** in custom formats, `[Attr]` goes on the parameter that takes the value instead of on the method, and
  every parameter of a factory needs one, except the text of a text node. Factories no longer set an attribute by the
  name of their parameter.
- **Breaking:** `.Attr` no longer writes an attribute whose method takes any string, such as `aria-label` or `role`.
- **Breaking:** `.Class(null)` and the other lists of names with a literal `null` are ambiguous now. Leave the call
  out instead, as it wrote nothing.
- A text node created by its factory, such as `Json.Str(null)`, writes nothing for null, as a plain string does.

### Fixed

- Hot Reload, such as with `dotnet watch`, failed on the second change to a template with "The assembly update failed".

## [0.1.0-preview.8] - 2026-11-06

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
- All packages support trimming and native AOT, checked by the trim and AOT analyzers and by a trimmed and a native
  app in the package smoke test. `ToResult` gets the content type from the generated code instead of by reflection.
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

[Unreleased]: https://github.com/aomader/TemplateDsl/compare/0.1.0...HEAD
[0.1.0]: https://github.com/aomader/TemplateDsl/releases/tag/0.1.0
[0.1.0-preview.8]: https://github.com/aomader/TemplateDsl/releases/tag/0.1.0-preview.8
[0.1.0-preview.2]: https://github.com/aomader/TemplateDsl/releases/tag/0.1.0-preview.2
[0.1.0-preview.1]: https://github.com/aomader/TemplateDsl/releases/tag/0.1.0-preview.1