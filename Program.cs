using System;
using System.CommandLine;
using System.Text.RegularExpressions;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using CUE4Parse_Conversion.Textures;
using SkiaSharp;
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
        var exportTextures = new Option<SKEncodedImageFormat?>("--textures", "If enabled textures will be exported in the selected format instead");

        var rootCommand = new RootCommand("uextract extracts object information from unreal engine games")
        {
            pakDirectory,
            outputDirectory,
            objectPath,
            unrealVersion,
            useRegex,
            exportTextures
        };

        rootCommand.SetHandler(
            (pakDirectory, outputDirectory, objectPath, unrealVersion, useRegex, exportTextures) =>
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
                        .ForEach((info) => Export(info.objects, info.path, outputDirectory, exportTextures));
                }
                else
                {
                    var objects = provider.LoadAllObjects(objectPath);
                    Export(objects, objectPath, outputDirectory, exportTextures);
                }
            },
            pakDirectory, outputDirectory, objectPath, unrealVersion, useRegex, exportTextures
        );

        rootCommand.Invoke(args);
    }

    static string EnsureOutputPath(DirectoryInfo outputDirectory, string outputPath)
    {
        var fullOutputPath = Path.Join(outputDirectory.FullName, outputPath);
        var directoryPath = Path.GetDirectoryName(fullOutputPath) ?? throw new Exception($"invalid output path for {fullOutputPath}");
        Console.WriteLine("{0}", Path.GetFullPath(fullOutputPath));
        Directory.CreateDirectory(directoryPath);
        return fullOutputPath;
    }

    static void Export(IEnumerable<UObject> objects, string objectPath, DirectoryInfo outputDirectory, SKEncodedImageFormat? exportTextures)
    {
        if (exportTextures is not null)
        {
            if (objects.Count() > 1)
            {
                Log.Warning($"More than one object at {objectPath}");
            }
            ExportObject(objects.First(), objectPath + $"", outputDirectory, exportTextures);
        }
        else
        {
            ExportObjects(objects, objectPath, outputDirectory);
        }
    }

    static void ExportObject(UObject uObject, string objectPath, DirectoryInfo outputDirectory, SKEncodedImageFormat? exportTextures)
    {
        if (exportTextures is SKEncodedImageFormat textureFormat && uObject is UTexture2D texture)
        {
            ExportTexture(texture, objectPath, outputDirectory, textureFormat);
        }
        else
        {
            Log.Warning($"UnspecifiedFormat UObject ({uObject.Class}) class for {objectPath}");
        }
    }


    static void ExportTexture(UTexture2D texture, string objectPath, DirectoryInfo outputDirectory, SKEncodedImageFormat textureFormat)
    {
        var outputPath = EnsureOutputPath(outputDirectory, objectPath + "." + textureFormat.ToString().ToLower());
        using (var pixmap = TextureDecoder.Decode(texture).PeekPixels())
        {
            using (var encoded = pixmap.Encode(textureFormat, 90))
            {
                // todo: stream this instead of this scary mess :(
                File.WriteAllBytes(outputPath, encoded.ToArray());
            }
        }
    }

    static void ExportObjects(IEnumerable<UObject> objects, string objectPath, DirectoryInfo outputDirectory)
    {
        var outputPath = EnsureOutputPath(outputDirectory, objectPath + ".json");
        var json = JsonConvert.SerializeObject(objects, Formatting.Indented);
        File.WriteAllText(outputPath, json);
    }
}
