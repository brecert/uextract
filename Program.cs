using System;
using System.CommandLine;
using System.Text.RegularExpressions;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;

namespace UExtract;

static class Program
{
    const string ZERO_64_CHAR = "0000000000000000000000000000000000000000000000000000000000000000";
    static readonly FGuid ZERO_GUID = new(0U);
    public static void Main(string[] args)
    {
        var pakDirectory = new Option<DirectoryInfo>("--pak-directory", "Where to load the `.pak` files from <GameRoot>/Content/Paks") { IsRequired = true }.ExistingOnly();
        var outputDirectory = new Option<DirectoryInfo>("--output", "The directory to export files to") { IsRequired = true };
        var objectPath = new Option<string>("--object", "The object path(s) to match for exporting") { IsRequired = true };
        var unrealVersion = new Option<EGame>("--unreal-version", "The unreal engine version the game uses") { IsRequired = true };
        var useRegex = new Option<bool>("--use-regex", "If enabled all objects paths matching the regex in `--object` will be exported");

        var rootCommand = new RootCommand("uextract extracts object information from unreal engine games")
        {
            pakDirectory,
            outputDirectory,
            objectPath,
            unrealVersion,
            useRegex
        };

        rootCommand.SetHandler(
            (pakDirectory, outputDirectory, objectPath, unrealVersion, useRegex) =>
            {
                Log.Logger = new LoggerConfiguration().WriteTo.Console(theme: AnsiConsoleTheme.Literate, restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Warning).CreateLogger();

                var provider = new DefaultFileProvider(pakDirectory, SearchOption.TopDirectoryOnly, false, new VersionContainer(unrealVersion));

                provider.Initialize();
                provider.SubmitKey(ZERO_GUID, new FAesKey(ZERO_64_CHAR));

                provider.LoadVirtualPaths();
                provider.LoadLocalization(ELanguage.English);

                if (useRegex)
                {
                    var regex = new Regex(objectPath) ?? throw new Exception("`--object` is not a valid regex");
                    provider.Files.Keys
                        .Where(path => regex.IsMatch(path))
                        .Select(path => (path, objects: provider.LoadAllObjects(path)))
                        .ToList()
                        .ForEach((info) => ExportObjects(info.objects, info.path, outputDirectory));
                }
                else
                {
                    var objects = provider.LoadAllObjects(objectPath);
                    ExportObjects(objects, objectPath, outputDirectory);
                }
            },
            pakDirectory, outputDirectory, objectPath, unrealVersion, useRegex
        );

        rootCommand.Invoke(args);
    }

    static void ExportObjects(IEnumerable<UObject> objects, string objectPath, DirectoryInfo outputDirectory)
    {
        var json = JsonConvert.SerializeObject(objects, Formatting.Indented);
        var outputPath = Path.Join(outputDirectory.FullName, objectPath + ".json");
        var directoryPath = Path.GetDirectoryName(outputPath) ?? throw new Exception($"invalid output path for {outputPath}");
        Console.WriteLine("{0}", Path.GetFullPath(outputPath));
        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(outputPath, json);
    }
}
