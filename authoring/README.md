# Unity Authoring Resources

This tree preserves the corresponding source, materials, Probe carrier prefabs,
control shaders and original Unity metadata for the Probe 0.2.2 resource package.
It also preserves Core's authoring DLL metadata. The resource source was exported
verbatim through Codex Bridge; no serialized asset was hand-edited.

DLL binaries are excluded from this Git tree. Either use the matching
`TomRenderInfrastructure-KKS-Authoring-0.1.1.zip` from the release, or build the
maintained solution and place the matching outputs at these preserved paths:

```text
Assets/TomRenderInfrastructure/Runtime/TomRender.Core.dll
Assets/Mods/TomLightingProbes/Runtime/TomProbe.Runtime.dll
```

Then import the `Assets` subtree into a compatible KKS Unity 2019.4 template,
keeping metadata/GUIDs. Use Unity APIs/Codex Bridge for native asset changes.
Do not import the game BepInEx entry-point DLLs as authoring scripts. The TomX
authoring mirrors remain Assembly-CSharp in TomX; BSDF keeps its own matching
Runtime authoring binding in its source overlay.

The existing resource Bundle assignment is
`studio/tom/tom_lighting_probes.unity3d`. The 0.1.1 distribution reuses its
accepted Bundle, while the authoring ZIP contains the matching new Core/Probe
runtime DLLs. Native asset/source/meta bytes retain their original GUIDs;
the read-only Codex Bridge export did not trigger a Bundle rebuild.
Feather's helper shader/Bundle source remains in the matching
[TomX source](https://github.com/tomTom1010-IEE/TomshadersX), not a second maintained
implementation here. Game/Unity/template SDK assets are external prerequisites.
