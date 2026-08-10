namespace SchemeGenerator;

public sealed record SpeciesMember(
    string CSharpName,
    string TsName,
    string CSharpType,
    string TsType,
    string? Initializer,
    string? Tooltip,
    bool IsAngleInRadians,
    bool ShouldGenerateSignal
);

public sealed record TsFormField(
    string CSharpName,
    string CSharpType,
    string TsName,
    string Label,
    string Kind,
    string TsType,
    string? Min,
    string? Max,
    string Step,
    string? Tooltip,
    bool IsAngle,
    bool IsVariance,
    string? VarianceTsName
);