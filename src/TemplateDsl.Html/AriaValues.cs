namespace TemplateDsl.Html;

/// <summary>The values of <c>aria-autocomplete</c>: how a text box suggests what can be entered.</summary>
public enum AriaAutoComplete
{
    /// <summary><c>none</c>: it suggests nothing.</summary>
    None,

    /// <summary><c>inline</c>: it completes the text in place.</summary>
    Inline,

    /// <summary><c>list</c>: it shows a list of suggestions.</summary>
    List,

    /// <summary><c>both</c>: a list, and the selected suggestion in place.</summary>
    Both,
}

/// <summary>The values of <c>aria-current</c>: which item of a set is the current one.</summary>
public enum AriaCurrent
{
    /// <summary><c>false</c>: not the current one.</summary>
    False,

    /// <summary><c>true</c>: the current one, of a kind none of the others name.</summary>
    True,

    /// <summary><c>page</c>: the current page, such as in a navigation.</summary>
    Page,

    /// <summary><c>step</c>: the current step of a process.</summary>
    Step,

    /// <summary><c>location</c>: the current location, such as in a flow chart.</summary>
    Location,

    /// <summary><c>date</c>: the current date, such as in a calendar.</summary>
    Date,

    /// <summary><c>time</c>: the current time, such as in a timetable.</summary>
    Time,
}

/// <summary>The values of <c>aria-haspopup</c>: whether, and what kind of, popup an element opens.</summary>
public enum AriaHasPopup
{
    /// <summary><c>false</c>: it opens none.</summary>
    False,

    /// <summary><c>true</c>: it opens a menu.</summary>
    True,

    /// <summary><c>menu</c></summary>
    Menu,

    /// <summary><c>listbox</c></summary>
    Listbox,

    /// <summary><c>tree</c></summary>
    Tree,

    /// <summary><c>grid</c></summary>
    Grid,

    /// <summary><c>dialog</c></summary>
    Dialog,
}

/// <summary>The values of <c>aria-invalid</c>: whether, and why, what was entered is wrong.</summary>
public enum AriaInvalid
{
    /// <summary><c>false</c>: it isn't.</summary>
    False,

    /// <summary><c>true</c>: it is.</summary>
    True,

    /// <summary><c>grammar</c>: it has a grammatical error.</summary>
    Grammar,

    /// <summary><c>spelling</c>: it has a spelling error.</summary>
    Spelling,
}

/// <summary>The values of <c>aria-live</c>: how a region that changes is announced.</summary>
public enum AriaLive
{
    /// <summary><c>off</c>: changes aren't announced.</summary>
    Off,

    /// <summary><c>polite</c>: changes are announced when the user is idle.</summary>
    Polite,

    /// <summary><c>assertive</c>: changes are announced at once.</summary>
    Assertive,
}

/// <summary>The values of <c>aria-orientation</c>, such as of a slider or a toolbar.</summary>
public enum AriaOrientation
{
    /// <summary><c>horizontal</c></summary>
    Horizontal,

    /// <summary><c>vertical</c></summary>
    Vertical,
}

/// <summary>The values of <c>aria-sort</c>: how the column of a table or grid is sorted.</summary>
public enum AriaSort
{
    /// <summary><c>none</c>: it isn't.</summary>
    None,

    /// <summary><c>ascending</c></summary>
    Ascending,

    /// <summary><c>descending</c></summary>
    Descending,

    /// <summary><c>other</c>: in another order.</summary>
    Other,
}
