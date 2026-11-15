using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TemplateDsl.Generators;
using Xunit;
using static TemplateDsl.Html.Elements;

namespace Tests;

public sealed partial class AriaPage(bool open, bool? checkedState, Role role, AriaCurrent current, double ratio) : IHtmlTemplate
{
    public IEnumerable<HtmlElement> Build() =>
    [
        Button.AriaExpanded(open).AriaControls("menu").AriaHasPopup(AriaHasPopup.Menu).AriaLabel($"Menu {ratio}") [ "x" ],
        Div.Role(Role.AlertDialog).AriaModal(true).AriaLabelledBy("title").AriaHidden(false),
        Div.Role("doc-chapter"),
        Div.Role(role).AriaCurrent(current),
        Span.Role(Role.Checkbox).AriaChecked(checkedState),
        Span.Role(Role.Checkbox).AriaChecked("mixed"),
        Div.Role(Role.ProgressBar).AriaValueNow(ratio).AriaValueMin(0).AriaValueMax(1).AriaLevel(2),
        Td.Aria("braillelabel", $"{ratio}").Aria("rowindextext", "Row 1").Aria("hidden", true)
    ];
}

/// <summary>
/// The ARIA attributes of WAI-ARIA 1.2 have methods with the types the specification gives them, and Aria writes the
/// others. Booleans are the words true and false, and only the attributes for people use the current culture.
/// </summary>
public class AriaTests
{
    [Fact]
    public void Aria_attributes_are_written_with_their_types()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");

        try
        {
            Assert.Equal(
                "<button aria-expanded=\"true\" aria-controls=\"menu\" aria-haspopup=\"menu\" aria-label=\"Menu 0,5\">x</button>"
                + "<div role=\"alertdialog\" aria-modal=\"true\" aria-labelledby=\"title\" aria-hidden=\"false\"></div>"
                + "<div role=\"doc-chapter\"></div>"
                + "<div role=\"tabpanel\" aria-current=\"page\"></div>"
                + "<span role=\"checkbox\" aria-checked=\"false\"></span>"
                + "<span role=\"checkbox\" aria-checked=\"mixed\"></span>"
                + "<div role=\"progressbar\" aria-valuenow=\"0.5\" aria-valuemin=\"0\" aria-valuemax=\"1\" aria-level=\"2\"></div>"
                + "<td aria-braillelabel=\"0,5\" aria-rowindextext=\"Row 1\" aria-hidden=\"true\"></td>",
                new AriaPage(true, false, Role.TabPanel, AriaCurrent.Page, 0.5).RenderToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void Null_leaves_a_boolean_aria_attribute_out() =>
        Assert.Contains("<span role=\"checkbox\"></span>", new AriaPage(false, null, Role.Tab, AriaCurrent.False, 1).RenderToString());

    [Theory]
    [InlineData("""=> [H.Div.Attr("aria-label", "x")];""", "'aria-label' has its own method for this kind of value, .AriaLabel(...), so it can't be used with Attr")]
    [InlineData("""=> [H.Div.Aria("label", "x")];""", "'aria-label' has its own method for this kind of value, .AriaLabel(...), so it can't be used with Aria")]
    [InlineData("""=> [H.Div.Aria("Braille", "x")];""", "'Braille' has an uppercase letter")]
    [InlineData("""=> [H.Div.Attr("role", "x")];""", ".Role(...), so it can't be used with Attr")]                 // .Role takes a string itself
    [InlineData("""=> [H.Span.Attr("aria-checked", "mixed")];""", ".AriaChecked(...), so it can't be used with Attr")]
    [InlineData("""=> [H.InputNumber.Attr("step", "any")];""", ".Step(...), so it can't be used with Attr")]
    public void Attributes_with_a_method_that_takes_any_string_are_written_with_it(string build, string expected)
    {
        var diagnostic = Assert.Single(Translate(build));

        Assert.Equal("TDSL004", diagnostic.Id);
        Assert.Contains(expected, diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("""=> [H.Div.Attr("aria-expanded", "{{open}}")];""")]     // a placeholder of a framework in the browser
    [InlineData("""=> [H.Div.Attr("aria-level", "{{level}}")];""")]
    [InlineData("""=> [H.Li.Attr("aria-current", "{{current}}")];""")]
    public void A_typed_attribute_takes_a_value_its_type_does_not_through_Attr(string build) =>
        Assert.Empty(Translate(build));

    private static System.Collections.Immutable.ImmutableArray<Diagnostic> Translate(string build)
    {
        var compilation = CSharpCompilation.Create(
            "Test",
            [CSharpSyntaxTree.ParseText($$"""
                using System.Collections.Generic;
                using TemplateDsl;
                using TemplateDsl.Html;
                using H = TemplateDsl.Html.Elements;

                public sealed partial class T : IHtmlTemplate
                {
                    public IEnumerable<HtmlElement> Build() {{build}}
                }
                """)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return CSharpGeneratorDriver.Create(new TemplateGenerator()).RunGenerators(compilation).GetRunResult().Diagnostics;
    }
}
