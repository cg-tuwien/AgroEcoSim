using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis;
using Agro;
using System.Text.RegularExpressions;

namespace SchemeGenerator;

public static partial class Reader
{
    public static ClassDeclarationSyntax ReadSpeciesSettingsClass(string sourceCode)
    {
        var tree = CSharpSyntaxTree.ParseText(sourceCode);
        var root = tree.GetCompilationUnitRoot();

        return root.DescendantNodes()
                   .OfType<ClassDeclarationSyntax>()
                   .FirstOrDefault(c => c.Identifier.Text == nameof(SpeciesSettings))
            ?? throw new InvalidOperationException("Could not find class SpeciesSettings.");
    }

    public static List<SpeciesMember> Extract(ClassDeclarationSyntax classNode)
    {
        var result = new List<SpeciesMember>();
        string? currentGroup = null;

        foreach (var classMember in classNode.Members)
        {
            switch (classMember)
            {
                case PropertyDeclarationSyntax property:
                {
                    var declaredGroup = GetSpeciesUIGroup(property);
                    if (!string.IsNullOrWhiteSpace(declaredGroup))
                        currentGroup = declaredGroup;

                    var member = TryReadProperty(property, currentGroup);
                    if (member is not null)
                        result.Add(member);
                }
                break;

                case FieldDeclarationSyntax field:
                {
                    var declaredGroup = GetSpeciesUIGroup(field);
                    if (!string.IsNullOrWhiteSpace(declaredGroup))
                        currentGroup = declaredGroup;

                    foreach (var member in TryReadField(field, currentGroup))
                        result.Add(member);
                }
                break;
            }
        }

        return result;
    }

    private static string? GetSpeciesUIGroup(MemberDeclarationSyntax syntax) => GetStringAttributeValue(syntax, nameof(SpeciesUIGroupAttribute), "SpeciesUIGroup");

    private static string? GetSpeciesUILabel(MemberDeclarationSyntax syntax) => GetStringAttributeValue(syntax, nameof(SpeciesUILabelAttribute), "SpeciesUILabel");

    private static string? GetSpeciesUITooltip(MemberDeclarationSyntax syntax) => GetStringAttributeValue(syntax, nameof(SpeciesUITooltipAttribute), "SpeciesUITooltip");

    private static bool GetSpeciesUIIgnore(MemberDeclarationSyntax syntax) => GetPlainAttribute(syntax, nameof(SpeciesUIIgnoreAttribute), "SpeciesUIIgnore");

    private static string? GetStringAttributeValue(MemberDeclarationSyntax syntax, params string[] acceptedNames)
    {
        var accepted = acceptedNames.Select(NormalizeAttributeName).ToHashSet(StringComparer.Ordinal);

        foreach (var attributeList in syntax.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var actualName = NormalizeAttributeName(attribute.Name.ToString());
                if (!accepted.Contains(actualName))
                    continue;

                var argumentList = attribute.ArgumentList;
                if (argumentList is null || argumentList.Arguments.Count == 0)
                    return null;

                // Prefer first positional argument:
                var positional = argumentList.Arguments.FirstOrDefault(a => a.NameEquals is null && a.NameColon is null);

                if (positional is not null)
                    return ReadStringExpression(positional.Expression);

                // Optional support for named argument style: [SpeciesUILabel(Value = "Plant height")]
                foreach (var arg in argumentList.Arguments)
                {
                    var name = arg.NameEquals?.Name.Identifier.Text;
                    if (name is "Value" or "Text" or "Label" or "Tooltip" or "Group")
                        return ReadStringExpression(arg.Expression);
                }
                return null;
            }
        }
        return null;
    }

    private static bool GetPlainAttribute(MemberDeclarationSyntax syntax, params string[] acceptedNames)
    {
        var accepted = acceptedNames.Select(NormalizeAttributeName).ToHashSet(StringComparer.Ordinal);

        foreach (var attributeList in syntax.AttributeLists)
            foreach (var attribute in attributeList.Attributes)
            {
                var actualName = NormalizeAttributeName(attribute.Name.ToString());
                return accepted.Contains(actualName);
            }

        return false;
    }

    private static string NormalizeAttributeName(string name)
    {
        var lastDot = name.LastIndexOf('.');
        if (lastDot >= 0)
        name = name[(lastDot + 1)..];

        if (name.EndsWith("Attribute", StringComparison.Ordinal))
            name = name[..^"Attribute".Length];
        return name;
    }

    private static string? ReadStringExpression(ExpressionSyntax expression)
    {
        return expression switch
        {
            LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression) => literal.Token.ValueText,
            // Supports nameof(SomeGroup), returns "SomeGroup".
            InvocationExpressionSyntax invocation when invocation.Expression.ToString() == "nameof" => ReadNameOfArgument(invocation),
            // Fallback. Useful during development, but not ideal for complex expressions.
            _ => expression.ToString().Trim('"')
        };
    }

    private static string? ReadNameOfArgument(InvocationExpressionSyntax invocation)
    {

        var arg = invocation.ArgumentList.Arguments.FirstOrDefault();
        if (arg is null)
            return null;

        return arg.Expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            _ => arg.Expression.ToString()
        };
    }

    private static SpeciesMember? TryReadProperty(PropertyDeclarationSyntax property, string? currentGroup)
    {
        if (!IsPublicInstance(property.Modifiers))
            return null;

        if (property.ExpressionBody is not null)
            return null;

        var name = property.Identifier.Text;
        var type = property.Type.ToString();
        var initializer = property.Initializer?.Value.ToString();

        return CreateMember(name, type, initializer, property, currentGroup);
    }

    private static IEnumerable<SpeciesMember> TryReadField(FieldDeclarationSyntax field, string? currentGroup)
    {
        if (!IsPublicInstance(field.Modifiers))
            yield break;

        var type = field.Declaration.Type.ToString();

        foreach (var variable in field.Declaration.Variables)
        {
            var name = variable.Identifier.Text;
            var initializer = variable.Initializer?.Value.ToString();

            var member = CreateMember(name, type, initializer, field, currentGroup);
            if (member is not null)
                yield return member;
        }
    }

    private static bool IsPublicInstance(SyntaxTokenList modifiers)
    {
        return modifiers.Any(SyntaxKind.PublicKeyword) && !modifiers.Any(SyntaxKind.StaticKeyword);
    }

    private static SpeciesMember? CreateMember(string csharpName, string csharpType, string? initializer, MemberDeclarationSyntax syntax, string? currentGroup)
    {
        initializer ??= csharpType switch
        {
            "float" or "double" or "int" or "byte" or "behavior" or nameof(SeasonalPhase) => "0",
            "bool" => "false",
            _ => "\"\""
        };

        if (!ShouldGenerate(csharpName, csharpType) || GetSpeciesUIIgnore(syntax))
            return null;

        var isAngle = IsAngleMember(csharpName, syntax);
        var tsName = ToTsName(csharpName, isAngle);
        var tsType = ToTsType(csharpType);

        var xmlSummary = GetXmlSummary(syntax);
        var explicitLabel = GetSpeciesUILabel(syntax);
        var explicitTooltip = GetSpeciesUITooltip(syntax);

        var usingXmlSummary = string.IsNullOrWhiteSpace(explicitLabel) && !string.IsNullOrWhiteSpace(xmlSummary);
        var label = !string.IsNullOrWhiteSpace(explicitLabel) ? explicitLabel : usingXmlSummary ? xmlSummary : ToLabel(csharpName, isAngle);
        var tooltip = !string.IsNullOrWhiteSpace(explicitTooltip) ? explicitTooltip : usingXmlSummary ? null : xmlSummary;

        return new SpeciesMember(
            CSharpName: csharpName,
            TsName: tsName,
            CSharpType: csharpType,
            TsType: tsType,
            Initializer: initializer,
            Group: currentGroup,
            Label: label,
            Tooltip: tooltip,
            IsAngleInRadians: isAngle,
            ShouldGenerateSignal: true
        );
    }

    private static bool ShouldGenerate(string csharpName, string csharpType)
    {
        if (csharpName is "DominanceFactors" || csharpType.EndsWith("[]") || csharpType.Contains("List<"))
            return false;

        if (csharpType.EndsWith("[]") || csharpType[^1] == '>')
            throw new Exception($"Arrays and generic types are not supported for species settings! The problematic entry is {csharpType} {csharpName}.");

        return csharpType switch
        {
            "float" => true,
            "double" => true,
            "int" => true,
            "short" => true,
            "sbyte" => true,
            "uint" => true,
            "ushort" => true,
            "byte" => true,
            "bool" => true,
            "string" => true,
            "Behavior" => true,
            nameof(SeasonalPhase) => true,
            _ => false
        };
    }

    private static string ToTsName(string csharpName, bool isAngle)
    {
        var camel = char.ToLowerInvariant(csharpName[0]) + csharpName[1..];

        if (camel == "behavior")
            camel += "Index";

        if (camel.Equals(nameof(SeasonalPhase), StringComparison.InvariantCultureIgnoreCase))
            camel += "Flags";

        if (!isAngle)
            return camel;

        if (camel.EndsWith("Var"))
            return camel[..^3] + "DegVar";

        return camel + "Deg";
    }

    private static bool IsAngleMember(string name, MemberDeclarationSyntax syntax)
    {
        if (name.Contains("Pitch", StringComparison.OrdinalIgnoreCase))
            return true;

        if (name.Contains("Roll", StringComparison.OrdinalIgnoreCase))
            return true;

        if (name.Contains("Angle", StringComparison.OrdinalIgnoreCase))
            return true;

        var trivia = syntax.GetLeadingTrivia().ToFullString();

        if (trivia.Contains("in radians", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static string ToTsType(string csharpType)
    {
        return csharpType switch
        {
            "float" or "double" or "long" or "int" or "short" or "sbyte" or "ulong" or "uint" or "ushort" or "byte" => "number",
            "bool" => "boolean",
            "string" => "string",
            "Behavior" or nameof(SeasonalPhase)  => "number",
            _ => "any"
        };
    }

    private static string ToTsDefaultValue(SpeciesMember member)
    {
        if (member.Initializer is null)
            return "undefined";

        var expr = member.Initializer.Trim();

        if (expr == "null")
            return "null";

        if (expr == "true" || expr == "false")
            return expr;

        if (expr.StartsWith("\""))
            return expr;

        if (expr.StartsWith(nameof(Behavior)))
        {
            var enumName = expr[(nameof(Behavior).Length + 1)..];
            var behaviors = Enum.GetValues<Behavior>();
            for(int i = 0; i < behaviors.Length; ++i)
                if (behaviors[i].ToString() == enumName)
                    return i.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (expr.StartsWith(nameof(SeasonalPhase)))
        {
            var enumName = expr[(nameof(SeasonalPhase).Length + 1)..];
            var flags = Enum.GetValues<SeasonalPhase>();
            for(int i = 0; i < flags.Length; ++i)
                if (flags[i].ToString() == enumName)
                    return i.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (member.IsAngleInRadians)
            return ConvertAngleInitializerToDegrees(expr);

        return ConvertNumericExpression(expr);
    }

    private static string ConvertAngleInitializerToDegrees(string expr)
    {
        expr = NormalizeNumberSyntax(expr);

        if (expr.Contains("* DegToRad"))
            return expr.Replace("* DegToRad", "").Trim();

        if (expr.Contains("DegToRad *"))
            return expr.Replace("DegToRad *", "").Trim();

        // If the C# default is already a raw radian number, convert it.
        var numericValue = TryEvaluateSimpleNumericExpression(expr);
        if (numericValue is not null)
            return FormatNumber(numericValue.Value * 180.0 / Math.PI);

        return "0";
    }

    private static string ConvertNumericExpression(string expr)
    {
        expr = NormalizeNumberSyntax(expr);

        var value = TryEvaluateSimpleNumericExpression(expr);
        if (value is not null)
            return FormatNumber(value.Value);

        return expr;
    }

    private static string NormalizeNumberSyntax(string expr)
    {
        if (expr == "float.MaxValue")
            return float.MaxValue.ToString();
        else if (expr == "float.MinValue")
            return float.MinValue.ToString();
        else if (expr == "double.MaxValue")
            return double.MaxValue.ToString();
        else if (expr == "double.MinValue")
            return double.MinValue.ToString();

        return expr
            .Replace("_", "")
            .Replace("MathF.PI", Math.PI.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("Math.PI", Math.PI.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("DegToRad", $"({Math.PI.ToString(System.Globalization.CultureInfo.InvariantCulture)} / 180.0)")
            .Replace("f", "", StringComparison.OrdinalIgnoreCase);
    }

    private static double? TryEvaluateSimpleNumericExpression(string expr)
    {
        try
        {
            var table = new System.Data.DataTable();
            var value = table.Compute(expr, "");
            return Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    private static string FormatNumber(double value)
    {
        return value.ToString("0.##########", System.Globalization.CultureInfo.InvariantCulture);
    }

    public static string GenerateInterface(IEnumerable<SpeciesMember> members)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("// <auto-generated>");
        sb.AppendLine("// Generated from SpeciesSettings.cs.");
        sb.AppendLine("// Do not edit manually.");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine();
        sb.AppendLine("import { signal } from \"@preact/signals\";");
        sb.AppendLine();
        sb.AppendLine("const DegToRad = Math.PI / 180.0;");
        sb.AppendLine("const RadToDeg = 180.0 / Math.PI;");
        sb.AppendLine();
        sb.AppendLine("export class SpeciesBase {");

        foreach (var member in members)
        {
            var defaultValue = ToTsDefaultValue(member);
            sb.AppendLine($"    {member.TsName} = signal({defaultValue});");
        }

        sb.AppendLine();
        sb.AppendLine("    public save() {");
        sb.AppendLine("        return {");

        foreach (var member in members)
        {
            var savedName = char.ToLowerInvariant(member.CSharpName[0]) + member.CSharpName[1..];

            if (member.IsAngleInRadians)
                sb.AppendLine($"            {savedName}: this.{member.TsName}.peek() * DegToRad,");
            else
                sb.AppendLine($"            {savedName}: this.{member.TsName}.peek(),");
        }

        sb.AppendLine("        };");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public load(s: any) {");

        foreach (var member in members)
        {
            var savedName = char.ToLowerInvariant(member.CSharpName[0]) + member.CSharpName[1..];

            if (member.IsAngleInRadians)
                sb.AppendLine($"        this.{member.TsName}.value = s.{savedName} * RadToDeg;");
            else
                sb.AppendLine($"        this.{member.TsName}.value = s.{savedName};");
        }

        sb.AppendLine("        return this;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public serialize() {");
        sb.AppendLine("        return {");

        foreach (var member in members)
        {
            if (member.IsAngleInRadians)
                sb.AppendLine($"            {member.CSharpName}: this.{member.TsName}.peek() * DegToRad,");
            else
                sb.AppendLine($"            {member.CSharpName}: this.{member.TsName}.peek(),");
        }

        sb.AppendLine("        };");
        sb.AppendLine("    }");

        sb.AppendLine("}");

        return sb.ToString();
    }

    private static bool ShouldGenerateFormField(SpeciesMember member) => member.TsType is "number" or "boolean" or "string" || member.CSharpType == "Behavior";

    private static string? GetXmlSummary(MemberDeclarationSyntax syntax)
    {
        var trivia = syntax.GetLeadingTrivia().ToFullString();

        var start = trivia.IndexOf("<summary>", StringComparison.OrdinalIgnoreCase);
        var end = trivia.IndexOf("</summary>", StringComparison.OrdinalIgnoreCase);

        if (start < 0 || end < 0 || end <= start)
            return null;

        var content = trivia.Substring(start + "<summary>".Length, end - start - "<summary>".Length);

        return content.Replace("///", "").Replace("\r", "").Replace("\n", " ").Trim();
    }

    private static string ToLabel(string csharpName, bool isAngle)
    {
        var text = InsertSpacesRegex().Replace(csharpName, "$1 $2");

        if (text.EndsWith("var", StringComparison.OrdinalIgnoreCase))
            text = text[..^3] + "Variance";

        if (isAngle && !text.Contains("deg", StringComparison.OrdinalIgnoreCase))
            text += " (deg)";

        return text;
    }
    private static (string? Min, string? Max, string Step) InferNumberConstraints(SpeciesMember member)
    {
        var name = member.CSharpName;

        if (name.Contains("Probability", StringComparison.Ordinal))
            return ("0", "1", "0.0001");

        if (name is nameof(SpeciesSettings.FloweringSeasonStart) or nameof(SpeciesSettings.FloweringSeasonEnd))
            return ("0", "1", "0.01");

        if (name is nameof(SpeciesSettings.RhizomeOffshootsPerSegment) or nameof(SpeciesSettings.InitialRhizomeAxes))
            return ("0", "32", "1");

        if (name == nameof(SpeciesSettings.PetioleSegmentCount))
            return ("1", null, "1");

        if (member.CSharpType is "int" or "byte")
            return ("0", null, "1");

        if (name.Contains("PerNode"))
            return ("1", null, "1");

        if (name.Contains("Pitch"))
            return ("0", "180", "0.1");

        if (name.Contains("Roll"))
            return ("0", "360", "0.1");

        if (name.Contains("Angle"))
            return ("0", "180", "0.1");

        if (name.Contains("Factor"))
            return ("0", "2", "0.1");

        if (name == nameof(SpeciesSettings.RootsDensity))
            return ("0", "1", "0.01");

        if (name.Contains("Gravitaxis"))
            return ("0", "1", "0.01");

        if (name.Contains("Bending"))
            return ("0", "1", "0.01");

        if (name.EndsWith("Var"))
            return ("0", null, "0.01");

        if (name.Contains("Radius"))
            return ("0", null, "0.001");

        if (name.Contains("Length"))
            return ("0", null, "0.001");

        if (name.Contains("Time") || name.Contains("Age"))
            return ("0", null, "1");

        return ("0", null, "0.1");
    }

    private static TsFormField ToFormField(SpeciesMember member)
    {
        var kind = member.CSharpType switch
        {
            "bool" => "checkbox",
            "string" => "text",
            "Behavior" => "behavior",
            nameof(SeasonalPhase) => "flags",
            _ => "number"
        };

        var (min, max, step) = kind == "number" ? InferNumberConstraints(member) : (null, null, "");

        return new TsFormField(
            CSharpName: member.CSharpName,
            CSharpType: member.CSharpType,
            TsName: member.TsName,
            Label: member.Label,
            Kind: kind,
            TsType: member.TsType,
            Min: min,
            Max: max,
            Step: step,
            Group: member.Group,
            Tooltip: member.Tooltip,
            IsAngle: member.IsAngleInRadians,
            IsVariance: member.CSharpName.EndsWith("Var"),
            VarianceTsName: null
        );
    }

    public static string GenerateForm(IEnumerable<SpeciesMember> sourceMembers)
    {
        var members = sourceMembers.Where(ShouldGenerateFormField).Select(ToFormField).ToList();

        var sb = new System.Text.StringBuilder();

        sb.AppendLine("// <auto-generated>");
        sb.AppendLine("// Generated from SpeciesSettings.cs.");
        sb.AppendLine("// Do not edit manually.");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine();
        sb.AppendLine("import { h, Fragment } from \"preact\";");
        sb.AppendLine("import appstate from \"../../appstate\";");
        sb.AppendLine("import { Signal } from \"@preact/signals\";");
        sb.AppendLine("import { SpeciesFloatField, SpeciesIntField, SpeciesBoolField, SpeciesNameFields, SpeciesBehaviorField, SpeciesFlagsField } from \"./SpeciesItemTemplate\";");
        sb.AppendLine("import { Species } from \"src/helpers/Species\";");
        sb.AppendLine();
        sb.AppendLine("export function GeneratedSpeciesFields(props: { species: Species; index: number }) {");
        sb.AppendLine("    const { species, index } = props;");
        sb.AppendLine();
        sb.AppendLine("    return <>");

        foreach (var line in GenerateRows(members))
            sb.AppendLine(line);

        sb.AppendLine("    </>;");
        sb.AppendLine("}");

        return sb.ToString();
    }

    const string FloatFieldTag = "SpeciesFloatField";
    const string IntFieldTag = "SpeciesIntField";
    const string BoolFieldTag = "SpeciesBoolField";
    const string StringFieldTag = "SpeciesStringField";
    const string StringFlagsTag = "SpeciesFlagsField";

    private static string GenerateGenericRow(TsFormField field, TsFormField? variance = null)
    {
        var tooltip = !string.IsNullOrWhiteSpace(field.Tooltip) ? $"tooltip={{{TsStringLiteral(field.Tooltip)}}}" : "";
        var tag = field.CSharpType switch
        {
            "float" or "double" => FloatFieldTag,
            "long" or "int" or "short" or "sbyte" or "ulong" or "uint" or "ushort" or "byte" => IntFieldTag,
            "string" => StringFieldTag,
            "bool" => BoolFieldTag,
            nameof(SeasonalPhase) => StringFlagsTag,
            _ => throw new Exception("Unknown field type.")
        };

        string result = "";

        switch (tag)
        {
            case FloatFieldTag: case IntFieldTag:
                var min = !string.IsNullOrWhiteSpace(field.Min) && field.Min != "0" ? $"min={{{field.Min}}}" : "";
                var max = !string.IsNullOrWhiteSpace(field.Max) ? $"max={{{field.Max}}}" : "";
                var v = variance != null ? $"variance={{{{ step: {variance.Step}, signal: species.{variance.TsName}}}}}" : "";
                result = $"<{tag} {min} {max} step={{{field.Step}}} speciesIndex={{index}} {tooltip} signal={{species.{field.TsName}}} label={{{TsStringLiteral(field.Label)}}} {v} />";
                break;
            case BoolFieldTag:
                result = $"<{tag} speciesIndex={{index}} {tooltip} signal={{species.{field.TsName}}} label={{{TsStringLiteral(field.Label)}}} />";
                break;
            case StringFlagsTag:
                var enumType = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .SelectMany(a => a.GetTypes())
                    .FirstOrDefault(t => t.IsEnum && t.Name == field.CSharpType);
                var names = Enum.GetNames(enumType);
                result = $"<{tag} speciesIndex={{index}} {tooltip} signal={{species.{field.TsName}}} label={{{TsStringLiteral(field.Label)}}} flags={{[{string.Join(",", names.Select(x => $"'{x}'"))}]}}/>";
                break;
        }

        return ReduceWhitespaces().Replace(result, " ");
    }

    private static string GenerateBehaviorRow() => $"<SpeciesBehaviorField speciesIndex={{index}} signal={{species.behaviorIndex}} options={{appstate.behaviors}} />";

    private static string GenerateNameRow(TsFormField nameField, TsFormField akaField) => $"<SpeciesNameFields speciesIndex={{index}} nameSignal={{species.{nameField.TsName}}} akaSignal={{species.{akaField.TsName}}} linksCount={{appstate.speciesLinksCount(species.{nameField.TsName}.value)}}/>";

    private static IEnumerable<string> GenerateRows(List<TsFormField> fields)
    {
        var byCSharpName = fields.ToDictionary(x => x.CSharpName);
        var emitted = new HashSet<string>();

        var akaFiled = fields.FirstOrDefault(x => x.CSharpName == nameof(SpeciesSettings.Aka));

        string? lastRenderedGroup = null;
        var hasRenderedAnyField = false;

        foreach (var field in fields)
        {
            if (emitted.Contains(field.CSharpName))
                continue;

            if (field.IsVariance)
                continue;

            if (ShouldEmitGroupSeparator(field, lastRenderedGroup, hasRenderedAnyField))
                yield return "<hr/>";

            if (!string.IsNullOrWhiteSpace(field.Group))
                lastRenderedGroup = field.Group;

            var varianceName = field.CSharpName + "Var";

            switch (field.CSharpName)
            {
                case nameof(SpeciesSettings.Name): yield return GenerateNameRow(field, akaFiled); emitted.Add(akaFiled.CSharpName); hasRenderedAnyField = true; break;
                case nameof(SpeciesSettings.Aka): yield return ""; break;
                case nameof(SpeciesSettings.Behavior): yield return GenerateBehaviorRow(); hasRenderedAnyField = true; break;
                default:
                {
                    if(byCSharpName.TryGetValue(varianceName, out var variance))
                    {
                        yield return GenerateGenericRow(field, variance);
                        emitted.Add(variance.CSharpName);
                    }
                    else
                        yield return GenerateGenericRow(field);
                }
                hasRenderedAnyField = true;
                break;
            }

            emitted.Add(field.CSharpName);
        }
    }

    private static bool ShouldEmitGroupSeparator(TsFormField field, string? lastRenderedGroup, bool hasRenderedAnyField)
        => hasRenderedAnyField && !string.IsNullOrWhiteSpace(field.Group) && !string.IsNullOrWhiteSpace(lastRenderedGroup) && !string.Equals(field.Group, lastRenderedGroup, StringComparison.Ordinal);

    private static string TsStringLiteral(string value) => System.Text.Json.JsonSerializer.Serialize(value);

    [GeneratedRegex(@"\s+")]
    private static partial Regex ReduceWhitespaces();
    [GeneratedRegex("([a-z])([A-Z])")]
    private static partial Regex InsertSpacesRegex();
}
