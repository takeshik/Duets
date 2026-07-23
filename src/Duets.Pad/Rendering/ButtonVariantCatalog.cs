namespace Duets.Pad.Rendering;

internal static class ButtonVariantCatalog
{
    private const string SupportedValues =
        "primary,secondary,success,info,warning,danger,light,dark,muted,blue,azure,indigo,"
        + "purple,pink,red,orange,yellow,lime,green,teal,cyan,x,facebook,twitter,linkedin,"
        + "google,youtube,vimeo,dribbble,github,instagram,pinterest,vk,rss,flickr,bitbucket,"
        + "tabler";

    public static string TypeScriptUnion { get; } =
        $"\"{SupportedValues.Replace(",", "\" | \"")}\"";

    public static string ValidationMessage { get; } =
        $"Button variant must be one of: {SupportedValues.Replace(",", ", ")}.";

    public static IEnumerable<string> SupportedVariants
    {
        get
        {
            var start = 0;
            while (start < SupportedValues.Length)
            {
                var separator = SupportedValues.IndexOf(',', start);
                if (separator < 0)
                {
                    yield return SupportedValues[start..];
                    yield break;
                }

                yield return SupportedValues[start..separator];
                start = separator + 1;
            }
        }
    }

    public static bool IsSupported(string value)
    {
        var start = 0;
        while (start < SupportedValues.Length)
        {
            var separator = SupportedValues.IndexOf(',', start);
            var end = separator < 0 ? SupportedValues.Length : separator;
            var length = end - start;
            if (
                value.Length == length
                && string.CompareOrdinal(SupportedValues, start, value, 0, length) == 0
            )
            {
                return true;
            }

            if (separator < 0)
            {
                return false;
            }

            start = separator + 1;
        }

        return false;
    }
}
