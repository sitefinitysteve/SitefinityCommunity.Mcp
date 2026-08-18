using System.Reflection;
using System.Xml.Linq;

namespace SitefinityCommunity.Mcp.Cli;

/// <summary>
/// `sitefinity-comm-mcp install-plugin --target &lt;path&gt;` — writes the embedded Sitefinity plugin
/// sources into a Sitefinity web app project and registers them in its .csproj, replacing the
/// PowerShell installer so the flow works from any shell on any OS with no repo checkout.
/// The plugin ships as source (not a DLL) so it compiles against the host project's own
/// Sitefinity assemblies — see the repo README for why.
/// </summary>
public static class PluginInstaller
{
    private const string RelativeDir = "Code\\Mcp\\SitefinityCommunity";
    private const string ResourcePrefix = "SitefinityCommunity.Mcp.PluginSource.";

    public static int Run(string[] args)
    {
        var explicitTarget = ParseTarget(args);
        var target = explicitTarget ?? Directory.GetCurrentDirectory();

        if (!Directory.Exists(target))
        {
            Console.Error.WriteLine($"Target project not found: {target}");
            return 1;
        }

        // A web app root always carries web.config. When the target is implied from the current
        // directory, its absence is a hard stop — silently scaffolding Code\Mcp\ into some random
        // directory would be worse than an error.
        var looksLikeWebRoot = File.Exists(Path.Combine(target, "web.config"));

        if (!looksLikeWebRoot)
        {
            if (explicitTarget is null)
            {
                Console.Error.WriteLine($"No web.config found in: {target}");
                Console.Error.WriteLine();
                Console.Error.WriteLine("This command needs to run from your Sitefinity web root (the folder containing");
                Console.Error.WriteLine("web.config and Global.asax). cd there and run it again, or point at it explicitly:");
                Console.Error.WriteLine("  sitefinity-comm-mcp install-plugin --target <path-to-sitefinity-web-root>");
                return 1;
            }

            Console.WriteLine($"Warning: '{target}' has no web.config — is this a Sitefinity web app root? " +
                "Continuing because --target was explicit.");
        }

        Console.WriteLine($"Installing into: {Path.GetFullPath(target)}");
        Console.WriteLine();

        var destDir = Path.Combine(target, "Code", "Mcp", "SitefinityCommunity");
        Directory.CreateDirectory(destDir);

        var assembly = Assembly.GetExecutingAssembly();
        var sources = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".cs", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        if (sources.Count == 0)
        {
            Console.Error.WriteLine("No embedded plugin sources found — this build is broken; report it as a bug.");
            return 1;
        }

        var written = new List<string>();

        foreach (var resource in sources)
        {
            var fileName = resource[ResourcePrefix.Length..];
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            var content = reader.ReadToEnd();

            var destPath = Path.Combine(destDir, fileName);
            var changed = !File.Exists(destPath) || File.ReadAllText(destPath) != content;

            if (changed)
            {
                File.WriteAllText(destPath, content);
            }

            Console.WriteLine($"  {(changed ? "updated " : "current ")} {fileName}");
            written.Add(fileName);
        }

        UpdateProjectFile(target, written);
        PrintGlobalAsaxInstructions(target);
        return 0;
    }

    private static string? ParseTarget(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--target", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }

        // Allow a bare positional path: `sitefinity-comm-mcp install-plugin C:\Path\To\WebApp`
        return args.Skip(1).FirstOrDefault(a => !a.StartsWith('-'));
    }

    /// <summary>
    /// Registers each plugin file as a <c>Compile</c> item in the target's .csproj (classic,
    /// non-SDK projects need explicit includes), pruning stale entries from earlier versions.
    /// SDK-style projects glob sources automatically, so those are left untouched.
    /// </summary>
    private static void UpdateProjectFile(string target, List<string> files)
    {
        var csproj = Directory.GetFiles(target, "*.csproj").FirstOrDefault();

        if (csproj is null)
        {
            Console.WriteLine("  No .csproj found — include the files in your project manually.");
            return;
        }

        var doc = XDocument.Load(csproj, LoadOptions.PreserveWhitespace);
        var root = doc.Root!;
        var ns = root.Name.Namespace;

        if (root.Attribute("Sdk") is not null)
        {
            Console.WriteLine($"  {Path.GetFileName(csproj)} is SDK-style — sources are globbed automatically, no edit needed.");
            return;
        }

        // Prune every existing entry under our folder, then re-add the current set.
        var stale = root.Descendants(ns + "Compile")
            .Where(c => (c.Attribute("Include")?.Value ?? string.Empty)
                .StartsWith(RelativeDir + "\\", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var entry in stale)
        {
            entry.Remove();
        }

        // Hand-formatted whitespace: the document is loaded with PreserveWhitespace so the rest of
        // the (often large) project file is left byte-for-byte untouched, which means our new nodes
        // must carry their own indentation.
        var itemGroup = new XElement(ns + "ItemGroup");

        foreach (var file in files)
        {
            itemGroup.Add(new XText("\n    "));
            itemGroup.Add(new XElement(ns + "Compile",
                new XAttribute("Include", RelativeDir + "\\" + file)));
        }

        itemGroup.Add(new XText("\n  "));

        var lastCompileGroup = root.Elements(ns + "ItemGroup")
            .LastOrDefault(g => g.Elements(ns + "Compile").Any());

        if (lastCompileGroup is not null)
        {
            lastCompileGroup.AddAfterSelf(new XText("\n  "), itemGroup);
        }
        else
        {
            root.Add(new XText("  "), itemGroup, new XText("\n"));
        }

        doc.Save(csproj);
        Console.WriteLine($"  {Path.GetFileName(csproj)}: {files.Count} Compile entries registered ({stale.Count} stale pruned).");
    }

    private static void PrintGlobalAsaxInstructions(string target)
    {
        Console.WriteLine();

        // The bootstrap hook usually lives in Global.asax.cs, but not always — some projects use a
        // separate HttpApplication class or startup file. Scan the likely spots (root + App_Start)
        // rather than assuming; when in doubt, print the instructions.
        var candidates = Directory.EnumerateFiles(target, "*.cs", SearchOption.TopDirectoryOnly)
            .Concat(Directory.Exists(Path.Combine(target, "App_Start"))
                ? Directory.EnumerateFiles(Path.Combine(target, "App_Start"), "*.cs", SearchOption.TopDirectoryOnly)
                : []);

        var wiredIn = candidates.FirstOrDefault(f =>
            File.ReadAllText(f).Contains("McpInit.Register", StringComparison.Ordinal));

        if (wiredIn is not null)
        {
            Console.WriteLine($"McpInit.Register() already wired in {Path.GetFileName(wiredIn)} — build the project and recycle the app pool.");
            return;
        }

        Console.WriteLine("Next: call McpInit.Register() once Sitefinity has bootstrapped — usually in " +
            "Global.asax.cs (or wherever your project handles Bootstrapper.Initialized):");
        Console.WriteLine();
        Console.WriteLine("    if (e.CommandName == \"Bootstrapped\")");
        Console.WriteLine("    {");
        Console.WriteLine("        SitefinityCommunity.Mcp.SitefinityPlugin.McpInit.Register();");
        Console.WriteLine("    }");
        Console.WriteLine();
        Console.WriteLine("Then build the project and recycle the app pool.");
    }
}
