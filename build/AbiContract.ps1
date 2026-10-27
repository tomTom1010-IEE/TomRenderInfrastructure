function Get-AbiContract($Snapshot) {
    $types = [Collections.Generic.List[object]]::new()
    function Visit-ContractType($type, [bool]$parentVisible) {
        $visibility = [int]$type.attributes -band 7
        $visible = $parentVisible -and ($visibility -in 1,2,4,7)
        $unityType = $type.base -in 'UnityEngine.MonoBehaviour','UnityEngine.ScriptableObject','BepInEx.BaseUnityPlugin'
        $serialized = ([int]$type.attributes -band 8192) -ne 0
        if ($visible -or $unityType -or $serialized) {
            $methods = @($type.methods | Where-Object { ([int]$_.attributes -band 7) -in 4,5,6 } | ForEach-Object {
                $method = [ordered]@{}
                foreach ($key in $_.Keys) { if ($key -ne 'body') { $method[$key] = $_[$key] } }
                $method
            })
            $methodNames = @($methods | ForEach-Object { $_.name })
            $fields = @($type.fields | Where-Object { ([int]$_.attributes -band 7) -in 4,5,6 })
            $serializedFields = @()
            if ($unityType -or $serialized) {
                $serializedFields = @($type.fields | Where-Object {
                    $flags = [int]$_.attributes
                    $serializeField = @($_.custom | Where-Object { $_.constructor -like '*UnityEngine.SerializeField*' }).Count -gt 0
                    (($flags -band 7) -eq 6 -or $serializeField) -and ($flags -band (16+32+64+128)) -eq 0
                })
            }
            $types.Add([ordered]@{
                name=$type.name; attributes=$type.attributes; base=$type.base; layout=$type.layout
                interfaces=$type.interfaces; custom=$type.custom; generics=$type.generics
                fields=$fields; serializedFields=$serializedFields; methods=$methods
                properties=@($type.properties | Where-Object { $_.getter -in $methodNames -or $_.setter -in $methodNames })
                events=@($type.events | Where-Object { $_.add -in $methodNames -or $_.remove -in $methodNames })
            })
        }
        foreach ($nested in $type.nested) { Visit-ContractType $nested $visible }
    }
    $shaderIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    function Visit-ShaderContract($type) {
        foreach ($method in $type.methods) {
            if ($null -eq $method.body) { continue }
            foreach ($instruction in $method.body.instructions) {
                if ($instruction -match 'ldstr "(_[A-Za-z0-9_]+)"$') { $null=$shaderIds.Add($Matches[1]) }
            }
        }
        foreach ($nested in $type.nested) { Visit-ShaderContract $nested }
    }
    foreach ($module in $Snapshot.modules) {
        foreach ($type in $module.types) { Visit-ContractType $type $true; Visit-ShaderContract $type }
    }
    [ordered]@{ identity=$Snapshot.identity; plugins=$Snapshot.plugins; types=@($types.ToArray()); shaderPropertyNames=@($shaderIds | Sort-Object) }
}

function Compare-AbiContract($Baseline, $Current, $ShaderProperties = $Current.shaderPropertyNames) {
    $issues = [Collections.Generic.List[string]]::new()
    function Same-Contract($a, $b) {
        ($a | ConvertTo-Json -Depth 60 -Compress) -ceq ($b | ConvertTo-Json -Depth 60 -Compress)
    }
    if ($Baseline.identity -cne $Current.identity) { $issues.Add('Assembly identity changed') }
    if (-not (Same-Contract $Baseline.plugins $Current.plugins)) { $issues.Add('BepInEx identity changed') }
    foreach ($type in $Baseline.types) {
        # Roslyn's private, serializable lambda-cache class is implementation
        # state, not a Unity-serialized type. Its field ordinals change with edits.
        if ($type.base -eq 'System.Object' -and $type.name -match '/<>c$' -and @($type.custom|Where-Object constructor -like '*CompilerGeneratedAttribute*').Count -eq 1) { continue }
        $currentType = @($Current.types | Where-Object { $_.name -ceq $type.name })
        if ($currentType.Count -ne 1) { $issues.Add("Missing / duplicate type: $($type.name)"); continue }
        $actual = $currentType[0]
        foreach ($key in 'attributes','base','layout','custom','generics','serializedFields') {
            if ($key -eq 'custom') {
                # Framework csc may emit class attributes in a different order.
                # Compare complete attribute payloads, not their metadata order.
                $beforeAttributes=@($type.custom|ForEach-Object {$_|ConvertTo-Json -Depth 60 -Compress}|Sort-Object)
                $afterAttributes=@($actual.custom|ForEach-Object {$_|ConvertTo-Json -Depth 60 -Compress}|Sort-Object)
                if (-not (Same-Contract $beforeAttributes $afterAttributes)) { $issues.Add("Changed $key on $($type.name)") }
            } elseif (-not (Same-Contract $type.$key $actual.$key)) { $issues.Add("Changed $key on $($type.name)") }
        }
        foreach ($interface in $type.interfaces) {
            if ($interface -cnotin $actual.interfaces) { $issues.Add("Removed interface $interface on $($type.name)") }
        }
        foreach ($kind in 'fields','methods','properties','events') {
            foreach ($member in $type.$kind) {
                $match = @($actual.$kind | Where-Object { $_.name -ceq $member.name })
                if ($match.Count -ne 1 -or -not (Same-Contract $member $match[0])) { $issues.Add("Changed $kind member: $($member.name)") }
            }
        }
    }
    foreach ($property in $Baseline.shaderPropertyNames) {
        if ($property -cnotin $ShaderProperties) { $issues.Add("Removed shader property contract: $property") }
    }
    return ,@($issues.ToArray())
}
