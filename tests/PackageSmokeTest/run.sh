#!/usr/bin/env bash
# Packs the real NuGet packages and consumes them from projects that know nothing about this
# repository's project references. Run it from anywhere; it is not part of the solution.
set -euo pipefail

repo="$(cd "$(dirname "$0")/../.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

feed="$work/feed"
packages="$work/packages"
mkdir -p "$feed"

for project in TemplateDsl TemplateDsl.Html TemplateDsl.AspNetCore; do
    dotnet build "$repo/src/$project" -c Release --nologo -v quiet
    dotnet pack "$repo/src/$project" -c Release --no-build --nologo -v quiet -o "$feed"
done

# MinVer computes the version from Git, and all packages get the same one, which the consumers then ask for.
version="$(basename "$feed"/TemplateDsl.[0-9]*.nupkg .nupkg)"
version="${version#TemplateDsl.}"
echo "--- packages, version $version"

echo "--- package contents"
for package in "$feed"/*.nupkg; do
    echo "$(basename "$package"):"
    unzip -l "$package" | grep -E '\.dll$|\.nuspec$|README' | awk '{print "  " $4}'
done

cat > "$work/NuGet.config" <<EOF
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$feed" />
  </packageSources>
</configuration>
EOF

consume() {
    local name="$1"
    local copy="$work/$name"
    cp -r "$repo/tests/PackageSmokeTest/$name" "$copy"
    # Isolated from this repository's Directory.Build.props, like a real consumer.
    dotnet run --project "$copy" -c Release --nologo -v quiet --property:RestorePackagesPath="$packages" --property:RestoreConfigFile="$work/NuGet.config" --property:TemplateDslVersion="$version"
}

# Runs a consumer and checks both its output and its exit code: a consumer that prints the right output and fails
# afterwards, such as with an exception, must fail the test too. In a command substitution passed as an argument,
# bash would ignore the exit code, even with set -e.
expect() {
    local description="$1" consumer="$2" expected="$3"
    local actual status=0
    actual="$(consume "$consumer")" || status=$?

    if [ "$status" -ne 0 ] || [ "$actual" != "$expected" ]; then
        echo "FAILED: $description"
        echo "  exit code: $status"
        echo "  expected:  $expected"
        echo "  actual:    $actual"
        exit 1
    fi
    echo "ok: $description"
}

echo "--- consumers"
expect "HTML package brings the generator along" \
    HtmlConsumer \
    '<div class="card">Hi &lt;you&gt;<div>one</div><div>two</div></div><div><p>slow</p></div><i>ctx</i>'

expect "core package alone supports a custom format" \
    CustomFormatConsumer \
    '[a\,b,c]'

expect "ASP.NET Core package renders a template as a result" \
    AspNetCoreConsumer \
    '201 text/html; charset=utf-8 <b>web</b>'
