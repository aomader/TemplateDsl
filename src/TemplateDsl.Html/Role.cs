using System.Runtime.Serialization;

namespace TemplateDsl.Html;

/// <summary>
/// The roles of WAI-ARIA 1.2, for <c>.Role(...)</c>: what an element is to assistive technology, such as
/// <c>Role.Dialog</c>. A role whose name is one word in ARIA but two in C#, such as <see cref="AlertDialog"/>, is written
/// as ARIA does: <c>alertdialog</c>. For a role of another specification, or several roles, <c>.Role</c> takes a string too.
/// </summary>
public enum Role
{
    /// <summary><c>role="alert"</c></summary>
    Alert,

    /// <summary><c>role="alertdialog"</c></summary>
    [EnumMember(Value = "alertdialog")]
    AlertDialog,

    /// <summary><c>role="application"</c></summary>
    Application,

    /// <summary><c>role="article"</c></summary>
    Article,

    /// <summary><c>role="banner"</c></summary>
    Banner,

    /// <summary><c>role="blockquote"</c></summary>
    Blockquote,

    /// <summary><c>role="button"</c></summary>
    Button,

    /// <summary><c>role="caption"</c></summary>
    Caption,

    /// <summary><c>role="cell"</c></summary>
    Cell,

    /// <summary><c>role="checkbox"</c></summary>
    Checkbox,

    /// <summary><c>role="code"</c></summary>
    Code,

    /// <summary><c>role="columnheader"</c></summary>
    [EnumMember(Value = "columnheader")]
    ColumnHeader,

    /// <summary><c>role="combobox"</c></summary>
    [EnumMember(Value = "combobox")]
    ComboBox,

    /// <summary><c>role="complementary"</c></summary>
    Complementary,

    /// <summary><c>role="contentinfo"</c></summary>
    [EnumMember(Value = "contentinfo")]
    ContentInfo,

    /// <summary><c>role="definition"</c></summary>
    Definition,

    /// <summary><c>role="deletion"</c></summary>
    Deletion,

    /// <summary><c>role="dialog"</c></summary>
    Dialog,

    /// <summary><c>role="document"</c></summary>
    Document,

    /// <summary><c>role="emphasis"</c></summary>
    Emphasis,

    /// <summary><c>role="feed"</c></summary>
    Feed,

    /// <summary><c>role="figure"</c></summary>
    Figure,

    /// <summary><c>role="form"</c></summary>
    Form,

    /// <summary><c>role="generic"</c></summary>
    Generic,

    /// <summary><c>role="grid"</c></summary>
    Grid,

    /// <summary><c>role="gridcell"</c></summary>
    [EnumMember(Value = "gridcell")]
    GridCell,

    /// <summary><c>role="group"</c></summary>
    Group,

    /// <summary><c>role="heading"</c></summary>
    Heading,

    /// <summary><c>role="img"</c></summary>
    Img,

    /// <summary><c>role="insertion"</c></summary>
    Insertion,

    /// <summary><c>role="link"</c></summary>
    Link,

    /// <summary><c>role="list"</c></summary>
    List,

    /// <summary><c>role="listbox"</c></summary>
    [EnumMember(Value = "listbox")]
    ListBox,

    /// <summary><c>role="listitem"</c></summary>
    [EnumMember(Value = "listitem")]
    ListItem,

    /// <summary><c>role="log"</c></summary>
    Log,

    /// <summary><c>role="main"</c></summary>
    Main,

    /// <summary><c>role="marquee"</c></summary>
    Marquee,

    /// <summary><c>role="math"</c></summary>
    Math,

    /// <summary><c>role="menu"</c></summary>
    Menu,

    /// <summary><c>role="menubar"</c></summary>
    [EnumMember(Value = "menubar")]
    MenuBar,

    /// <summary><c>role="menuitem"</c></summary>
    [EnumMember(Value = "menuitem")]
    MenuItem,

    /// <summary><c>role="menuitemcheckbox"</c></summary>
    [EnumMember(Value = "menuitemcheckbox")]
    MenuItemCheckbox,

    /// <summary><c>role="menuitemradio"</c></summary>
    [EnumMember(Value = "menuitemradio")]
    MenuItemRadio,

    /// <summary><c>role="meter"</c></summary>
    Meter,

    /// <summary><c>role="navigation"</c></summary>
    Navigation,

    /// <summary><c>role="none"</c></summary>
    None,

    /// <summary><c>role="note"</c></summary>
    Note,

    /// <summary><c>role="option"</c></summary>
    Option,

    /// <summary><c>role="paragraph"</c></summary>
    Paragraph,

    /// <summary><c>role="presentation"</c></summary>
    Presentation,

    /// <summary><c>role="progressbar"</c></summary>
    [EnumMember(Value = "progressbar")]
    ProgressBar,

    /// <summary><c>role="radio"</c></summary>
    Radio,

    /// <summary><c>role="radiogroup"</c></summary>
    [EnumMember(Value = "radiogroup")]
    RadioGroup,

    /// <summary><c>role="region"</c></summary>
    Region,

    /// <summary><c>role="row"</c></summary>
    Row,

    /// <summary><c>role="rowgroup"</c></summary>
    [EnumMember(Value = "rowgroup")]
    RowGroup,

    /// <summary><c>role="rowheader"</c></summary>
    [EnumMember(Value = "rowheader")]
    RowHeader,

    /// <summary><c>role="scrollbar"</c></summary>
    [EnumMember(Value = "scrollbar")]
    ScrollBar,

    /// <summary><c>role="search"</c></summary>
    Search,

    /// <summary><c>role="searchbox"</c></summary>
    [EnumMember(Value = "searchbox")]
    SearchBox,

    /// <summary><c>role="separator"</c></summary>
    Separator,

    /// <summary><c>role="slider"</c></summary>
    Slider,

    /// <summary><c>role="spinbutton"</c></summary>
    [EnumMember(Value = "spinbutton")]
    SpinButton,

    /// <summary><c>role="status"</c></summary>
    Status,

    /// <summary><c>role="strong"</c></summary>
    Strong,

    /// <summary><c>role="subscript"</c></summary>
    Subscript,

    /// <summary><c>role="superscript"</c></summary>
    Superscript,

    /// <summary><c>role="switch"</c></summary>
    Switch,

    /// <summary><c>role="tab"</c></summary>
    Tab,

    /// <summary><c>role="table"</c></summary>
    Table,

    /// <summary><c>role="tablist"</c></summary>
    [EnumMember(Value = "tablist")]
    TabList,

    /// <summary><c>role="tabpanel"</c></summary>
    [EnumMember(Value = "tabpanel")]
    TabPanel,

    /// <summary><c>role="term"</c></summary>
    Term,

    /// <summary><c>role="textbox"</c></summary>
    [EnumMember(Value = "textbox")]
    TextBox,

    /// <summary><c>role="time"</c></summary>
    Time,

    /// <summary><c>role="timer"</c></summary>
    Timer,

    /// <summary><c>role="toolbar"</c></summary>
    [EnumMember(Value = "toolbar")]
    ToolBar,

    /// <summary><c>role="tooltip"</c></summary>
    [EnumMember(Value = "tooltip")]
    ToolTip,

    /// <summary><c>role="tree"</c></summary>
    Tree,

    /// <summary><c>role="treegrid"</c></summary>
    [EnumMember(Value = "treegrid")]
    TreeGrid,

    /// <summary><c>role="treeitem"</c></summary>
    [EnumMember(Value = "treeitem")]
    TreeItem,
}
