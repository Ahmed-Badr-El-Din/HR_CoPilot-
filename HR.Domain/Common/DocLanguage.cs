namespace HR.Domain.Common;

public enum DocLanguage
{
    En = 0,
    Ar = 1,
}

public static class DocLanguageExtensions
{
    public static string ToBcp47(this DocLanguage language) => language switch
    {
        DocLanguage.En => "en",
        DocLanguage.Ar => "ar",
        _ => "en",
    };

    /// <summary>
    /// Text direction used for RTL rendering (Twist T1).
    /// </summary>
    public static string ToHtmlDir(this DocLanguage language) => language == DocLanguage.Ar ? "rtl" : "ltr";
}
