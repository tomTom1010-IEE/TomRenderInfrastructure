using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

#pragma warning disable 0649 // Fields populated by JsonUtility, not C# assignments.

// Explicit, bounded authoring integration and validation. No Bundle build or deployment.
public static class TomRenderInfrastructureBridge
{
    [Serializable] sealed class AuthoringFile { public string source,destination,kind,sha256; }
    [Serializable] sealed class Candidate { public int schema;public string status,sourceFingerprint,auditSha256,buildReceiptSha256;public AuthoringFile[] files; }
    [Serializable] sealed class Gate { public bool passed;public string sourceFingerprint,buildReceiptSha256; }
    [Serializable] sealed class ScriptIdentity { public string path,guid,type,assembly;public long localFileId; }
    [Serializable] sealed class ScriptContract { public ScriptIdentity[] identities; }
    [Serializable] sealed class ImportReport { public string status,sourceFingerprint;public List<string> files=new List<string>();public List<ScriptIdentity> before=new List<ScriptIdentity>(); }
    [Serializable] sealed class RegressionReport { public bool passed;public string presetSha256,baselineSha256;public List<ScriptIdentity> identities=new List<ScriptIdentity>(); }
    static readonly string epoch=Guid.NewGuid().ToString("N");
    public static string Status()
    {
        return "epoch="+epoch+"; compiling="+UnityEditor.EditorApplication.isCompiling+"; loaded="+(validation!=null)+"; hash="+(loadedHash??"none");
    }
    public static string Reload()
    {
        UnityEditor.EditorApplication.delayCall += UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation;
        return "Explicit script compilation requested from epoch="+epoch+"; verify a changed epoch with statustomrendercore before loading new diagnostic DLLs. No scene save or Bundle build.";
    }
    static string loadedHash;
    static Assembly validation;
    static string Hash(string path)
    {
        using(var sha=SHA256.Create())using(var input=File.OpenRead(path))return BitConverter.ToString(sha.ComputeHash(input)).Replace("-","");
    }
    static ScriptIdentity ReadIdentity(ScriptIdentity expected)
    {
        foreach(var script in AssetDatabase.LoadAllAssetsAtPath(expected.path).OfType<MonoScript>())
        {
            var type=script.GetClass();if(type==null || type.FullName!=expected.type)continue;
            string guid;long fileId;AssetDatabase.TryGetGUIDAndLocalFileIdentifier(script,out guid,out fileId);
            return new ScriptIdentity{path=expected.path,guid=guid,localFileId=fileId,type=type.FullName,assembly=type.Assembly.GetName().Name};
        }
        throw new InvalidOperationException("Unresolved original script identity: "+expected.path+" / "+expected.type);
    }
    public static string IntegrateStep2(string projectRoot,string output)
    {return Integrate(projectRoot,output,2,false);}
    public static string IntegrateStep3(string projectRoot,string output,bool coreOnly)
    {return Integrate(projectRoot,output,3,coreOnly);}
    public static string IntegrateStep4(string projectRoot,string output,bool coreOnly)
    {return Integrate(projectRoot,output,4,coreOnly);}
    static string Integrate(string projectRoot,string output,int step,bool coreOnly)
    {
        if(Directory.Exists(output))throw new InvalidOperationException("Use a new import evidence directory");
        string unityRoot=Directory.GetParent(Application.dataPath).FullName;
        string candidatePath=Path.Combine(projectRoot,"artifacts/authoring/step"+step+"-candidate.json");
        var candidate=JsonUtility.FromJson<Candidate>(File.ReadAllText(candidatePath));
        string audit=Path.Combine(projectRoot,"artifacts/audit/report.json"),receipt=Path.Combine(projectRoot,"artifacts/build-receipt.json");
        var gate=JsonUtility.FromJson<Gate>(File.ReadAllText(audit));
        if(candidate.schema!=1 || candidate.status!="ready" || !gate.passed || candidate.sourceFingerprint!=gate.sourceFingerprint
            || Hash(audit)!=candidate.auditSha256.ToUpperInvariant() || Hash(receipt)!=candidate.buildReceiptSha256.ToUpperInvariant()
            || candidate.buildReceiptSha256!=gate.buildReceiptSha256)throw new InvalidOperationException("Stale / incompatible authoring gate");
        var allowed=new Dictionary<string,string>{
            {"Assets/TomRenderInfrastructure/Runtime/TomRender.Core.dll","artifacts/bin/TomRender.Core/Release/TomRender.Core.dll"},
            {"Assets/Mods/TomShadersBSDF/Runtime/TomBSDF.Runtime.dll","artifacts/bin/TomBSDF.Runtime/Release/TomBSDF.Runtime.dll"},
            {"Assets/TomSceneEffects/Runtime/DofCompatibility.cs","src/SceneEffects/TomSceneEffects.Runtime/DofCompatibility.cs"},
            {"Assets/TomSceneEffects/Runtime/TomDofOpaqueRelay.cs","src/SceneEffects/TomSceneEffects.Runtime/TomDofOpaqueRelay.cs"}};
        if(step>=3)
        {
            allowed.Add("Assets/Mods/TomShadersX/Runtime/TomHairFeatherCamera.cs","src/TomX/TomHairFeather/TomHairFeatherCamera.cs");
            allowed.Add("Assets/Mods/TomShadersX/Runtime/TomSkinSDFHead.cs","src/TomX/TomSkinSDF/TomSkinSDFHead.cs");
            allowed.Add("Assets/Mods/TomShadersX/Runtime/TomTessellationBounds.cs","src/TomX/TomTessellation/TomTessellationBounds.cs");
        }
        if(step==4)allowed.Add("Assets/Mods/TomLightingProbes/Runtime/TomProbe.Runtime.dll","artifacts/bin/TomProbe.Runtime/Release/TomProbe.Runtime.dll");
        if(candidate.files==null || candidate.files.Length!=allowed.Count || candidate.files.Select(f=>f.destination).Distinct().Count()!=allowed.Count)throw new InvalidOperationException("Unexpected authoring file set");
        var selected=candidate.files.Where(file=>!coreOnly || file.destination=="Assets/TomRenderInfrastructure/Runtime/TomRender.Core.dll").ToArray();
        foreach(var file in candidate.files)
            if(!allowed.ContainsKey(file.destination) || allowed[file.destination]!=file.source || Hash(Path.Combine(projectRoot,file.source))!=file.sha256.ToUpperInvariant())throw new InvalidOperationException("Authoring source mismatch: "+file.source);
        var contract=JsonUtility.FromJson<ScriptContract>(File.ReadAllText(Path.Combine(projectRoot,"migration/phase1/authoring-identities.json")));
        var report=new ImportReport{status="preflight",sourceFingerprint=candidate.sourceFingerprint};
        foreach(var expected in contract.identities)
        {
            var actual=ReadIdentity(expected);
            if(actual.guid!=expected.guid || actual.localFileId!=expected.localFileId || actual.assembly!=expected.assembly)throw new InvalidOperationException("Original authoring identity drift: "+expected.type);
            report.before.Add(actual);
        }
        Directory.CreateDirectory(output);
        foreach(var file in candidate.files)
        {
            string target=Path.Combine(unityRoot,file.destination);
            if(File.Exists(target)){string backup=Path.Combine(output,"original",file.destination);Directory.CreateDirectory(Path.GetDirectoryName(backup));File.Copy(target,backup,false);}
        }
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach(var folder in new[]{"Assets/TomRenderInfrastructure","Assets/TomRenderInfrastructure/Runtime"})
                if(!AssetDatabase.IsValidFolder(folder))AssetDatabase.CreateFolder(Path.GetDirectoryName(folder).Replace('\\','/'),Path.GetFileName(folder));
            foreach(var file in selected)
            {
                string target=Path.Combine(unityRoot,file.destination);
                if(!File.Exists(target) || Hash(target)!=file.sha256.ToUpperInvariant())File.Copy(Path.Combine(projectRoot,file.source),target,true);
                AssetDatabase.ImportAsset(file.destination,ImportAssetOptions.ForceUpdate);
                report.files.Add(file.destination);
            }
        }
        finally{AssetDatabase.StopAssetEditing();}
        report.status="imported-awaiting-script-domain-verification";
        File.WriteAllText(Path.Combine(output,"import-report.json"),JsonUtility.ToJson(report,true));
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        return Path.Combine(output,"import-report.json");
    }
    static void LoadValidation(string projectRoot)
    {
        string core=Path.Combine(projectRoot,"artifacts/bin/TomRender.Core/Release/TomRender.Core.dll");
        string test=Path.Combine(projectRoot,"artifacts/bin/TomRender.UnityValidation/Release/TomRender.UnityValidation.dll");
        string fingerprint=Hash(core)+Hash(test);
        if(validation!=null && loadedHash!=fingerprint)throw new InvalidOperationException("Diagnostic DLL changed. Reload the script domain; never reuse stale code.");
        if(validation!=null)return;
        var loaded=AppDomain.CurrentDomain.GetAssemblies().Where(a=>a.GetName().Name=="TomRender.Core").ToArray();
        if(loaded.Length>1)throw new InvalidOperationException("Multiple Core assemblies are loaded; refuse split ownership");
        if(loaded.Length==1)
        {
            if(string.IsNullOrEmpty(loaded[0].Location) || !File.Exists(loaded[0].Location) || Hash(loaded[0].Location)!=Hash(core))throw new InvalidOperationException("Loaded Core is unverified/stale; reload the script domain");
        }
        else Assembly.Load(File.ReadAllBytes(core));
        validation=Assembly.Load(File.ReadAllBytes(test));loadedHash=fingerprint;
    }
    public static string Observe(string projectRoot,string output)
    {
        LoadValidation(projectRoot);
        var method=validation.GetType("TomRenderValidation.ObserverValidation",true).GetMethod("Run",BindingFlags.Public|BindingFlags.Static);
        try{return (string)method.Invoke(null,new object[]{output});}
        catch(TargetInvocationException error){throw error.InnerException??error;}
    }
    public static string ValidateStep2(string projectRoot,string output)
    {
        LoadValidation(projectRoot);
        var method=validation.GetType("TomRenderValidation.Step2Validation",true).GetMethod("Run",BindingFlags.Public|BindingFlags.Static);
        try
        {
            string result=(string)method.Invoke(null,new object[]{output});
            var observe=validation.GetType("TomRenderValidation.ObserverValidation",true).GetMethod("Run",BindingFlags.Public|BindingFlags.Static);
            observe.Invoke(null,new object[]{Path.Combine(output,"presets")});
            var regression=new RegressionReport();
            var contract=JsonUtility.FromJson<ScriptContract>(File.ReadAllText(Path.Combine(projectRoot,"migration/phase1/authoring-identities.json")));
            foreach(var expected in contract.identities)
            {
                var actual=ReadIdentity(expected);
                if(actual.guid!=expected.guid || actual.localFileId!=expected.localFileId || actual.assembly!=expected.assembly)throw new InvalidOperationException("Post-import authoring identity drift: "+expected.type);
                regression.identities.Add(actual);
            }
            regression.presetSha256=Hash(Path.Combine(output,"presets/baseline-presets.png"));
            regression.baselineSha256=Hash(Path.Combine(projectRoot,"artifacts/integration/accepted-step1/observer/baseline-presets.png"));
            regression.passed=regression.presetSha256==regression.baselineSha256;
            File.WriteAllText(Path.Combine(output,"post-import-report.json"),JsonUtility.ToJson(regression,true));
            if(!regression.passed)throw new InvalidOperationException("Six original preset images changed after authoring import");
            return result;
        }
        catch(TargetInvocationException error){throw error.InnerException??error;}
    }
    public static string ValidateStep3(string projectRoot,string output,bool baseline)
    {
        LoadValidation(projectRoot);
        var method=validation.GetType("TomRenderValidation.Step3Validation",true).GetMethod("Run",BindingFlags.Public|BindingFlags.Static);
        try{return (string)method.Invoke(null,new object[]{output,baseline});}
        catch(TargetInvocationException error){throw error.InnerException??error;}
    }
    public static string ValidateStep4(string projectRoot,string output,bool baseline)
    {
        LoadValidation(projectRoot);
        var method=validation.GetType("TomRenderValidation.Step4Validation",true).GetMethod("Run",BindingFlags.Public|BindingFlags.Static);
        try{return (string)method.Invoke(null,new object[]{output,baseline});}
        catch(TargetInvocationException error){throw error.InnerException??error;}
    }
}
