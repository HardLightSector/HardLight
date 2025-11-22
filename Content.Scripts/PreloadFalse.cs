namespace Content.Scripts;

public sealed class PreloadFalse
{
    public static async Task CreateFiles()
    {
        Console.WriteLine("Input your target directory:");
        var directoryPath = Console.ReadLine();
        if (directoryPath == null)
            return;

        var files = Directory.GetFiles(directoryPath, "*.png");
        foreach (var file in files)
        {
            const string text = "preload: false\n";
            await File.WriteAllTextAsync($"{file}.yml", text);
        }
    }
}
