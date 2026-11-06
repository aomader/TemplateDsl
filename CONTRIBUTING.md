# Contributing

Issues and pull requests are welcome. For a larger change, open an issue first, so that we can agree on the design
before you spend time on it. Report security problems privately, as described in [SECURITY.md](SECURITY.md).

## Building and testing

You need the .NET 10 SDK.

```sh
dotnet build TemplateDsl.slnx            # warnings are errors
dotnet test TemplateDsl.slnx
dotnet run tools/GenerateHtmlElements.cs --check
tests/PackageSmokeTest/run.sh            # packs the packages and uses them like a consumer would
```

The smoke test catches what project references hide, such as a generator that doesn't reach consumers of a package. It
also publishes an app trimmed, and as native code where `clang` is installed, as on CI, and runs it.

The [CI workflow](.github/workflows/ci.yml) builds, tests, and checks generated HTML on Linux and Windows for every pull request; the package smoke test runs on Linux only.

## Coverage

Every line of the four packages under `src/` is covered by tests, and the pipeline keeps it that way:

```sh
dotnet test TemplateDsl.slnx --settings coverage.runsettings --results-directory TestResults/coverage
dotnet run tools/CheckCoverage.cs TestResults/coverage    # fails and lists the lines below 100%
```

- Only the packages are measured, not the samples or the tests.
- The generators mostly run inside the compiler, where they can't be measured. `RepositoryCompilationTests` runs
  them over the HTML format and the tests, as the build does, so that their coverage counts.
- What only exists for `Build`, which is compiled and never runs, is marked `[ExcludeFromCodeCoverage]`: the elements,
  the factories in `H`, and the members the format generator adds for type-checking. New ones get the attribute too.
- Branch coverage is reported but not enforced. `--branches` lists the branches no test takes. The few left are
  compiler machinery, such as the paths of `await` in a `finally`, or null checks of Roslyn APIs that are kept against
  crashes in the IDE.

## Where things are

- `src/TemplateDsl`: the core: the format attributes, the template interfaces and the render methods.
- `src/TemplateDsl.Generators`: the source generators and the analyzer. They know nothing about HTML: everything
  about a format comes from its attributes.
- `src/TemplateDsl.Html`: the HTML format. Its elements are generated from `elements.txt` by
  `dotnet run tools/GenerateHtmlElements.cs`. Change the list, not the generated files.
- `src/TemplateDsl.AspNetCore`: templates as results of endpoints and controller actions.
- `samples/`: the samples, and the benchmarks against Razor Pages, Blazor and Razor Slices.

## Guidelines

- **Tests:** a change in behavior comes with a test, and a new diagnostic with tests of when it is and isn't reported.
- **Performance:** for a change to the generator or the render methods, run the benchmarks before and after, and
  include both results in the pull request. Run them more than once, since single runs vary by several percent:

  ```sh
  dotnet run -c Release --project samples/TemplateDsl.Benchmarks -- --filter '*'
  ```

- **Diagnostics:** a new one gets the next free ID in `Diagnostics.cs`, a line in
  `src/TemplateDsl.Generators/AnalyzerReleases.Unshipped.md`, and a page in `docs/diagnostics`.
- **Public API:** before 1.0 it can still change, but a breaking change goes into the changelog with how to migrate.
- **Style:** type parameters have descriptive names, such as `TContext` or `TItem`, never a bare `T`.
- **Analyzers:** the packages run every .NET analyzer and must document their public API, the tests and samples the
  recommended ones. An exception goes into `.editorconfig`, or a `[SuppressMessage]` on the member, with the reason.
- **Changelog:** add a line under "Unreleased" in [CHANGELOG.md](CHANGELOG.md) for anything a user would notice.

## Releasing

The version of all packages is `Version` in `Directory.Build.props`, and they are always released together. The
[release workflow](.github/workflows/release.yml) publishes the version on `main` when it has no Git tag yet, and then
tags the commit with it. Merges that keep a released version publish nothing. A release, also of a preview, is one pull request:

Run the release tool with the new version, in a branch for that pull request:

```sh
dotnet run tools/SetVersion.cs 0.1.0-preview.2
```

It sets `Version`, turns "Unreleased" in `CHANGELOG.md` into the version and today's date under a new, empty
"Unreleased", and updates the links at its end. For a stable version only, such as `0.1.0`, it also moves the rules of
`AnalyzerReleases.Unshipped.md` into a `## Release 0.1.0` section of `AnalyzerReleases.Shipped.md`. A preview leaves them
unshipped, because those release headers only take plain version numbers. It changes nothing if the version isn't
higher than the current one, is tagged already, or if "Unreleased" lists no changes.

CI checks these notes in every pull request that changes the version, so a new version can only be merged with them.
The release workflow relies on those CI checks. If a release fails, the next merge releases the version, such
as the one that fixes the failure, with everything merged until then. To try again without merging, start the workflow
by hand.
