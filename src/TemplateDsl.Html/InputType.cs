namespace TemplateDsl.Html;

/// <summary>
/// The types of <c>&lt;input&gt;</c>, for <c>H.Input.Type(...)</c> when the type is only known at runtime. Each is
/// written in lowercase, with a dash between words: <see cref="DatetimeLocal"/> is <c>datetime-local</c>. When the
/// type is known while writing the template, <c>H.InputEmail</c> and the like take only what that type allows.
/// </summary>
public enum InputType
{
    /// <summary><c>type="text"</c></summary>
    Text,

    /// <summary><c>type="search"</c></summary>
    Search,

    /// <summary><c>type="tel"</c></summary>
    Tel,

    /// <summary><c>type="url"</c></summary>
    Url,

    /// <summary><c>type="email"</c></summary>
    Email,

    /// <summary><c>type="password"</c></summary>
    Password,

    /// <summary><c>type="date"</c></summary>
    Date,

    /// <summary><c>type="month"</c></summary>
    Month,

    /// <summary><c>type="week"</c></summary>
    Week,

    /// <summary><c>type="time"</c></summary>
    Time,

    /// <summary><c>type="datetime-local"</c></summary>
    DatetimeLocal,

    /// <summary><c>type="number"</c></summary>
    Number,

    /// <summary><c>type="range"</c></summary>
    Range,

    /// <summary><c>type="color"</c></summary>
    Color,

    /// <summary><c>type="checkbox"</c></summary>
    Checkbox,

    /// <summary><c>type="radio"</c></summary>
    Radio,

    /// <summary><c>type="file"</c></summary>
    File,

    /// <summary><c>type="submit"</c></summary>
    Submit,

    /// <summary><c>type="image"</c></summary>
    Image,

    /// <summary><c>type="reset"</c></summary>
    Reset,

    /// <summary><c>type="button"</c></summary>
    Button,

    /// <summary><c>type="hidden"</c></summary>
    Hidden,
}
