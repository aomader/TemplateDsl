using System.ComponentModel;

namespace TemplateDsl;

/// <summary>
/// Base class of the nodes of every format. A format derives its own base class from this. Nodes only
/// describe a template in its Build method, which the source generator compiles: they aren't rendered.
/// </summary>
public abstract class TemplateNode;

/// <summary>Used by generated members that only exist so that Build type-checks.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class TemplateBuild
{
    /// <summary>The error that Build-only members throw if they run anyway, which they only can by calling Build by hand.</summary>
    public static InvalidOperationException NotExecutable() =>
        new("Build is compiled by the source generator and can't be executed. Use the generated render methods.");
}
