using System.Text;
using System.Text.RegularExpressions;

namespace Content.Scripts;

public sealed class FileAttributions
{
    public static async Task Attribute()
    {
        Console.WriteLine("Input your target directory:");
        var directoryPath = Console.ReadLine();
        if (directoryPath == null)
            return;

        Console.WriteLine("Input your target source:");
        var sourcePath = Console.ReadLine();
        if (sourcePath == null)
            return;

        var defaultLicense = "CC-BY-SA-3.0";
        Console.WriteLine($"Input your target license (Press Enter for \"{defaultLicense}\"):");
        var license = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(license))
            license = defaultLicense;

        var defaultCopyright = "Taken from S.P.L.U.R.T-tg";
        Console.WriteLine($"Input your target copyright (Press Enter for \"{defaultCopyright}\"):");
        var copyright = Console.ReadLine() ?? defaultCopyright;
        if (string.IsNullOrWhiteSpace(copyright))
            copyright = defaultCopyright;

        var directories = Directory.GetDirectories(directoryPath).Prepend(directoryPath);
        foreach (var directory in directories)
        {
            var yml = new StringBuilder();
            var files = new List<string>();
            foreach (var file in Directory.EnumerateFiles(directory, "*.ogg", SearchOption.TopDirectoryOnly))
            {
                files.Add(Path.GetFileName(file));
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*.png", SearchOption.TopDirectoryOnly))
            {
                files.Add(Path.GetFileName(file));
            }

            files.Sort(new StringIntComparer());
            foreach (var file in files)
            {
                var relative = Path.GetRelativePath(directoryPath, directory);
                if (relative == ".")
                    relative = string.Empty;

                var source = Path.Join(sourcePath, relative, file).Replace('\\', '/');
                yml.AppendLine($"""
                    - files: ["{file}"]
                      license: "{license}"
                      copyright: "{copyright}"
                      source: "{source}"

                    """);
            }

            var attributions = Path.Join(directory, "attributions.yml");
            File.Delete(attributions);
            await File.WriteAllTextAsync(attributions, yml.ToString());
        }
    }
}

// Taken from https://github.com/conradakunga/BlogCode/tree/master/StringSorters
public sealed partial class StringIntComparer : IComparer<string?>
{
    private static readonly Regex _decimalRegex = MatchDecimalRegex();
    private static readonly Regex _leadingStringRegex = MatchLeadingStringRegex();

    public int Compare(string? left, string? right)
    {
        if (left == right) return 0;
        if (left is null) return -1;
        if (right is null) return 1;

        // First, try grab both leading strings
        var leftStringMatch = _leadingStringRegex.Match(left);
        var rightStringMatch = _leadingStringRegex.Match(right);

        // if both matches didn't succeed, use the normal string comparison
        if (!leftStringMatch.Success && !rightStringMatch.Success)
            return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);

        // if only one match succeeded, use normal string comparison
        if (leftStringMatch.Success != rightStringMatch.Success)
            return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);

        // Here, both matches succeeded. Compare the captured leading strings
        var comparison = string.Compare(leftStringMatch.Groups[1].Value, rightStringMatch.Groups[1].Value,
            StringComparison.OrdinalIgnoreCase);

        // If the leading strings are different, don't bother going any further
        if (comparison != 0)
            return comparison;

        // If both leading strings are the same, now compare the numbers

        // Find the first number in each string
        var leftDecimalMatch = _decimalRegex.Match(left);
        var rightDecimalMatch = _decimalRegex.Match(right);

        if (leftDecimalMatch.Success && rightDecimalMatch.Success)
        {
            // Numbers were found for both. Compare those
            return decimal.Parse(leftDecimalMatch.Value).CompareTo(decimal.Parse(rightDecimalMatch.Value));
        }

        // Use the default string comparison
        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"\d+(\.\d+)?", RegexOptions.Compiled)]
    private static partial Regex MatchDecimalRegex();


    [GeneratedRegex(@"^(\w+)\s*\d+", RegexOptions.Compiled)]
    private static partial Regex MatchLeadingStringRegex();
}
