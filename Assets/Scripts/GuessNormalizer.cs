using System.Globalization;
using System.Linq;
using System.Text;


public static class GuessNormalizer
{
    // Normalize to a canonical key: lower, no diacritics, no punctuation/spaces.
    public static string Key(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        string s = raw.Trim().ToLowerInvariant();


        // Replace gender symbols with letters to support inputs like "nidoran f" / "nidoran female".
        s = s.Replace('♀', 'f').Replace('♂', 'm');


        // Decompose and remove diacritics (e.g., Farfetch’d apostrophes, Mr. Mime periods, etc.)
        string formD = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var ch in formD)
        {
            var uc = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (uc != UnicodeCategory.NonSpacingMark)
            {
                if (char.IsLetterOrDigit(ch)) sb.Append(ch);
                // Treat spaces and punctuation as nothing; we already made them implicit.
            }
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}