function Convert-AttributeValue($Value) {
    if ($null -eq $Value) { return $null }
    if ($Value -is [Mono.Cecil.CustomAttributeArgument]) {
        return [ordered]@{ type=$Value.Type.FullName; value=(Convert-AttributeValue $Value.Value) }
    }
    if ($Value -is [Array]) { return ,@($Value | ForEach-Object { Convert-AttributeValue $_ }) }
    if ($Value -is [Mono.Cecil.TypeReference]) { return $Value.FullName }
    return $Value
}

function Get-Attributes($Owner) {
    return ,@($Owner.CustomAttributes | ForEach-Object {
        [ordered]@{
            constructor=$_.Constructor.FullName
            arguments=@($_.ConstructorArguments | ForEach-Object { Convert-AttributeValue $_ })
            fields=@($_.Fields | Sort-Object Name | ForEach-Object { [ordered]@{ name=$_.Name; argument=(Convert-AttributeValue $_.Argument) } })
            properties=@($_.Properties | Sort-Object Name | ForEach-Object { [ordered]@{ name=$_.Name; argument=(Convert-AttributeValue $_.Argument) } })
        }
    })
}

function Get-GenericParameters($Owner) {
    return ,@($Owner.GenericParameters | ForEach-Object {
        [ordered]@{ name=$_.Name; attributes=[int]$_.Attributes; constraints=@($_.Constraints | ForEach-Object { if ($_.PSObject.Properties['ConstraintType']) { $_.ConstraintType.FullName } else { $_.FullName } }); custom=(Get-Attributes $_) }
    })
}

function Convert-MetadataName([string]$Name) {
    $Name -replace '<PrivateImplementationDetails>\{[0-9A-Fa-f-]{36}\}', '<PrivateImplementationDetails>{compiler-generated}'
}

function Get-TypeSnapshot($Type) {
    [ordered]@{
        name=(Convert-MetadataName $Type.FullName); attributes=[int]$Type.Attributes; base=$(if ($Type.BaseType) { $Type.BaseType.FullName })
        layout=@($Type.PackingSize, $Type.ClassSize); custom=(Get-Attributes $Type); generics=(Get-GenericParameters $Type)
        interfaces=@($Type.Interfaces | ForEach-Object { $_.InterfaceType.FullName })
        fields=@($Type.Fields | ForEach-Object {
            [ordered]@{ name=(Convert-MetadataName $_.FullName); attributes=[int]$_.Attributes; offset=$_.Offset; constant=(Convert-AttributeValue $_.Constant); initial=[Convert]::ToBase64String($_.InitialValue); custom=(Get-Attributes $_) }
        })
        methods=@($Type.Methods | ForEach-Object {
            $method = $_
            $body = $null
            if ($method.HasBody) {
                $body = [ordered]@{
                    maxStack=$method.Body.MaxStackSize; initLocals=$method.Body.InitLocals
                    locals=@($method.Body.Variables | ForEach-Object { $_.VariableType.FullName })
                    instructions=@($method.Body.Instructions | ForEach-Object { if ($_.Operand -is [Mono.Cecil.MemberReference]) { Convert-MetadataName $_.ToString() } else { $_.ToString() } })
                    handlers=@($method.Body.ExceptionHandlers | ForEach-Object {
                        $h = $_
                        [ordered]@{
                            type=[int]$h.HandlerType; catch=$(if ($h.CatchType) { $h.CatchType.FullName })
                            regions=@('TryStart','TryEnd','HandlerStart','HandlerEnd','FilterStart' | ForEach-Object { if ($h.$_) { $h.$_.Offset } else { -1 } })
                        }
                    })
                }
            }
            [ordered]@{
                name=$method.FullName; attributes=[int]$method.Attributes; implAttributes=[int]$method.ImplAttributes
                callingConvention=[int]$method.CallingConvention; custom=(Get-Attributes $method); generics=(Get-GenericParameters $method)
                returnCustom=(Get-Attributes $method.MethodReturnType)
                parameters=@($method.Parameters | ForEach-Object { [ordered]@{ name=$_.Name; type=$_.ParameterType.FullName; attributes=[int]$_.Attributes; constant=(Convert-AttributeValue $_.Constant); custom=(Get-Attributes $_) } })
                body=$body
            }
        })
        properties=@($Type.Properties | ForEach-Object {
            [ordered]@{ name=$_.FullName; attributes=[int]$_.Attributes; custom=(Get-Attributes $_); getter=$(if ($_.GetMethod) { $_.GetMethod.FullName }); setter=$(if ($_.SetMethod) { $_.SetMethod.FullName }) }
        })
        events=@($Type.Events | ForEach-Object {
            [ordered]@{ name=$_.FullName; attributes=[int]$_.Attributes; custom=(Get-Attributes $_); add=$(if ($_.AddMethod) { $_.AddMethod.FullName }); remove=$(if ($_.RemoveMethod) { $_.RemoveMethod.FullName }) }
        })
        nested=@($Type.NestedTypes | ForEach-Object { Get-TypeSnapshot $_ })
    }
}

function Get-AssemblySnapshot([string]$Path) {
    $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($Path)
    try {
        [ordered]@{
            identity=$assembly.Name.FullName
            custom=(Get-Attributes $assembly)
            plugins=@($assembly.MainModule.Types | ForEach-Object {
                $t = $_
                $t.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'BepInEx.BepInPlugin' } | ForEach-Object {
                    [ordered]@{ type=$t.FullName; guid=$_.ConstructorArguments[0].Value; name=$_.ConstructorArguments[1].Value; version=$_.ConstructorArguments[2].Value }
                }
            })
            modules=@($assembly.Modules | ForEach-Object {
                [ordered]@{
                    name=$_.Name; kind=[int]$_.Kind; runtime=$_.RuntimeVersion; architecture=[int]$_.Architecture
                    attributes=[int]$_.Attributes; custom=(Get-Attributes $_)
                    references=@($_.AssemblyReferences | ForEach-Object { $_.FullName })
                    resources=@($_.Resources | ForEach-Object {
                        if ($_ -isnot [Mono.Cecil.EmbeddedResource]) { throw "Unexpected non-embedded resource in $Path" }
                        [ordered]@{ name=$_.Name; attributes=[int]$_.Attributes; data=[Convert]::ToBase64String($_.GetResourceData()) }
                    })
                    types=@($_.Types | ForEach-Object { Get-TypeSnapshot $_ })
                }
            })
        }
    } finally { $assembly.Dispose() }
}
