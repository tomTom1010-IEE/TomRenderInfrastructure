using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

// Explicit, read-only export of the Probe resource source and preserved metadata.
public static class TomRenderPublicationExport
{
    [Serializable] public sealed class Entry { public string path, sha256; public long bytes; }
    [Serializable] public sealed class Inventory
    {
        public string unity;
        public List<Entry> files = new List<Entry>();
    }

    public static string Export(string destination)
    {
        string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string allowed = Path.GetFullPath(Path.Combine(project, "Build/Source")) + Path.DirectorySeparatorChar;
        destination = Path.GetFullPath(destination);
        if (!destination.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)
            || Directory.Exists(destination))
            throw new InvalidOperationException("Choose a new export directory inside Build/Source.");

        const string probe = "Assets/Mods/TomLightingProbes";
        foreach (string path in AssetDatabase.FindAssets("t:Prefab", new[] { probe + "/Prefab" })
            .Select(AssetDatabase.GUIDToAssetPath))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!prefab || prefab.GetComponentsInChildren<Transform>(true)
                .SelectMany(t => t.GetComponents<Component>()).Any(c => !c))
                throw new InvalidOperationException("Missing Probe authoring component: " + path);
        }

        var files = new List<string>();
        foreach (string folder in new[] { "Shaders", "Material", "Prefab", "Runtime", "List", "Tooltips" })
        {
            files.AddRange(Directory.GetFiles(probe + "/" + folder, "*", SearchOption.AllDirectories)
                .Where(p => !p.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)
                    && !p.EndsWith(".pdb.meta", StringComparison.OrdinalIgnoreCase)));
            files.Add(probe + "/" + folder + ".meta");
        }
        files.Add(probe + "/manifest.xml"); files.Add(probe + "/manifest.xml.meta");
        files.Add(probe + ".meta");
        const string core = "Assets/TomRenderInfrastructure/Runtime/TomRender.Core.dll";
        files.Add(core); files.Add(core + ".meta");
        files.Add("Assets/TomRenderInfrastructure.meta");
        files.Add("Assets/TomRenderInfrastructure/Runtime.meta");
        foreach (string file in files)
            if (!File.Exists(file)) throw new FileNotFoundException("Required authoring source missing", file);

        var inventory = new Inventory { unity = Application.unityVersion };
        foreach (string source in files.Distinct().OrderBy(p => p, StringComparer.Ordinal))
        {
            string target = Path.Combine(destination, source);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            FileUtil.CopyFileOrDirectory(source, target);
            string hash = Hash(source);
            if (Hash(target) != hash) throw new IOException("Authoring export mismatch: " + source);
            inventory.files.Add(new Entry { path = source.Replace('\\', '/'), sha256 = hash,
                bytes = new FileInfo(target).Length });
        }
        File.WriteAllText(Path.Combine(destination, "SOURCE-INVENTORY.json"), JsonUtility.ToJson(inventory, true));
        return inventory.files.Count + " authoring source/resource files exported verbatim; no asset edits or Bundle build.";
    }

    static string Hash(string path)
    {
        using (var stream = File.OpenRead(path))
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
}
