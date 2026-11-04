# Security

TemplateDsl escapes what templates output, so a bug in it can lead to cross-site scripting in the apps that use it.
Please report such problems privately, not in a public issue.

## Reporting a vulnerability

Report it through [GitHub's private vulnerability reporting](https://github.com/aomader/TemplateDsl/security/advisories/new).
Include the template, the input and the output you got, and what you expected. The fix
will be released with an advisory that credits you, unless you prefer not to be named.

## What counts

- Text or an attribute value that is written unescaped, or escaped in a way a browser reads differently than intended.
- An attribute name, element or other markup that input can inject although it is not passed to `H.Raw`.
- Output that a template's types say can't happen, such as a value of a typed attribute that breaks out of its quotes.

These are not vulnerabilities, because they are documented to be the template's responsibility:

- Anything passed to `H.Raw(...)`.
- Values that are escaped correctly but are dangerous where they are used, such as a `javascript:` URL in `.Href(...)`,
  code in an event handler attribute like `.Attr("onclick", ...)`, CSS in `.Style(...)`, a document in `.Srcdoc(...)`,
  or the contents of `<script>` and `<style>`.
- The output of a custom format's escape rules, which say what is escaped, and of attributes that declare no `Escape`.

## Supported versions

Fixes go into the latest release. Before 1.0, there are no fixes for older versions.
