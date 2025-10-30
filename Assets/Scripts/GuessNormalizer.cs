using System.Globalization;
using System.Linq;
using System.Text;

public static class GuessNormalizer
{
    public static string Key(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;
        string s = raw.Trim().ToLowerInvariant();

        s = s.Replace('♀', 'f').Replace('♂', 'm');

        string formD = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var ch in formD)
        {
            var uc = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (uc != UnicodeCategory.NonSpacingMark)
            {
                if (char.IsLetterOrDigit(ch))
                    sb.Append(ch);
            }
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
