using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis;
using Agro;

namespace SchemeGenerator;

public static class Reader
{
    public static ClassDeclarationSyntax ReadSpeciesSettingsClass(string sourceCode)
    {
        var tree = CSharpSyntaxTree.ParseText(sourceCode);
        var root = tree.GetCompilationUnitRoot();

        return root
                .DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .FirstOrDefault(c => c.Identifier.Text == nameof(SpeciesSettings))
                ?? throw new InvalidOperationException("Could not find class SpeciesSettings.");
    }

    public static List<SpeciesMember> Extract(ClassDeclarationSyntax classNode)
    {
        var result = new List<SpeciesMember>();

        foreach (var property in classNode.Members.OfType<PropertyDeclarationSyntax>())
        {
            var member = TryReadProperty(property);
            if (member is not null)
                result.Add(member);
        }

        foreach (var field in classNode.Members.OfType<FieldDeclarationSyntax>())
        {
            foreach (var member in TryReadField(field))
                result.Add(member);
        }

        return result;
    }

    private static SpeciesMember? TryReadProperty(PropertyDeclarationSyntax property)
    {
        if (!IsPublicInstance(property.Modifiers))
            return null;

        if (property.ExpressionBody is not null)
            return null;

        var name = property.Identifier.Text;
        var type = property.Type.ToString();
        var initializer = property.Initializer?.Value.ToString();

        return CreateMember(name, type, initializer, property);
    }

    private static IEnumerable<SpeciesMember> TryReadField(FieldDeclarationSyntax field)
    {
        if (!IsPublicInstance(field.Modifiers))
            yield break;

        var type = field.Declaration.Type.ToString();

        foreach (var variable in field.Declaration.Variables)
        {
            var name = variable.Identifier.Text;
            var initializer = variable.Initializer?.Value.ToString();

            var member = CreateMember(name, type, initializer, field);
            if (member is not null)
                yield return member;
        }
    }

    private static bool IsPublicInstance(SyntaxTokenList modifiers)
    {
        return modifiers.Any(SyntaxKind.PublicKeyword) && !modifiers.Any(SyntaxKind.StaticKeyword);
    }

    private static SpeciesMember? CreateMember(
        string csharpName,
        string csharpType,
        string? initializer,
        MemberDeclarationSyntax syntax)
    {
        initializer ??= csharpType switch
        {
            "float" or "double" or "int" or "byte" or "behavior" => "0",
            "bool" => "false",
            _ => "\"\""
        };

        if (!ShouldGenerate(csharpName, csharpType))
            return null;

        var isAngle = IsAngleMember(csharpName, syntax);
        var tsName = ToTsName(csharpName, isAngle);
        var tsType = ToTsType(csharpType);

        return new SpeciesMember(
            CSharpName: csharpName,
            TsName: tsName,
            CSharpType: csharpType,
            TsType: tsType,
            Initializer: initializer,
            Tooltip: null,
            IsAngleInRadians: isAngle,
            ShouldGenerateSignal: true
        );
    }

    private static bool ShouldGenerate(string csharpName, string csharpType)
    {
        if (csharpName is "DominanceFactors")
            return false;

        if (csharpType.EndsWith("[]"))
            return false;

        if (csharpType.Contains("List<"))
            return false;

        return csharpType switch
        {
            "float" => true,
            "double" => true,
            "int" => true,
            "byte" => true,
            "bool" => true,
            "string" => true,
            "Behavior" => true,
            _ => false
        };
    }

    private static string ToTsName(string csharpName, bool isAngle)
    {
        var camel = char.ToLowerInvariant(csharpName[0]) + csharpName[1..];

        if (camel == "behavior")
            camel += "Index";

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

        var trivia = syntax.GetLeadingTrivia().ToFullString();

        if (trivia.Contains("in radians", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static string ToTsType(string csharpType)
    {
        return csharpType switch
        {
            "float" => "number",
            "double" => "number",
            "int" => "number",
            "byte" => "number",
            "bool" => "boolean",
            "string" => "string",
            "Behavior" => "number",
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
            var behaviors = Enum.GetValues(typeof(Behavior));
            for(int i = 0; i < behaviors.Length; ++i)
                if (expr == $"{nameof(Behavior)}.Default")
                    return i.ToString();
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

    private static bool ShouldGenerateFormField(SpeciesMember member)
    {
        return member.TsType is "number" or "boolean" or "string" || member.CSharpType == "Behavior";
    }

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
        var text = System.Text.RegularExpressions.Regex.Replace(csharpName, "([a-z])([A-Z])", "$1 $2");

        if (text.EndsWith("var", StringComparison.OrdinalIgnoreCase))
            text = text[..^3] + "Variance";

        if (isAngle && !text.Contains("deg", StringComparison.OrdinalIgnoreCase))
            text += " (deg)";

        return text;
    }
    private static (string? Min, string? Max, string Step) InferNumberConstraints(SpeciesMember member)
    {
        var name = member.CSharpName;

        if (member.CSharpType is "int" or "byte")
            return ("0", null, "1");

        if (name.Contains("PerNode"))
            return ("1", null, "1");

        if (name.Contains("Pitch"))
            return ("0", "180", "0.1");

        if (name.Contains("Roll"))
            return ("0", "360", "0.1");

        if (name.Contains("Factor"))
            return ("0", "2", "0.1");

        if (name.Contains("Density"))
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
            _ => "number"
        };

        var (min, max, step) = kind == "number"
            ? InferNumberConstraints(member)
            : (null, null, "");

        var label = ToLabel(member.CSharpName, member.IsAngleInRadians);

        return new TsFormField(
            CSharpName: member.CSharpName,
            CSharpType: member.CSharpType,
            TsName: member.TsName,
            Label: label,
            Kind: kind,
            TsType: member.TsType,
            Min: min,
            Max: max,
            Step: step,
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
        sb.AppendLine("import { SpeciesFloatField, SpeciesIntField, SpeciesNameFields, SpeciesBehaviorField } from \"./SpeciesItemTemplate\";");
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

    private static string GenerateGenericRow(TsFormField field)
    {
        var min = !string.IsNullOrWhiteSpace(field.Min) && field.Min != "0" ? $"min={{{field.Min}}}" : "";
        var max = !string.IsNullOrWhiteSpace(field.Max) ? $"max={{{field.Max}}}" : "";
        var tooltip = !string.IsNullOrWhiteSpace(field.Tooltip) ? $"tooltip=\"{field.Tooltip}\"" : "";
        var tag = field.CSharpType switch
        {
            "float" or "double" => "SpeciesFloatField",
            "int" => "SpeciesIntField",
            "string" => "SpeciesStringField",
            "bool" => "SpeciesBoolField",
            _ => throw new Exception("Unknown field type.")
        };

        return $"<{tag} {min} {max} step={{{field.Step}}} speciesIndex={{index}} {tooltip} signal={{species.{field.TsName}}} label=\"{field.Label}\"/>";
    }

    private static string GenerateBehaviorRow() => $"<SpeciesBehaviorField speciesIndex={{index}} signal={{species.behaviorIndex}} options={{appstate.behaviors}} />";

    private static string GenerateNameRow(TsFormField nameField, TsFormField akaField) => $"<SpeciesNameFields speciesIndex={{index}} nameSignal={{species.{nameField.TsName}}} akaSignal={{species.{akaField.TsName}}} linksCount={{appstate.speciesLinksCount(species.{nameField.TsName}.value)}}/>";

    private static IEnumerable<string> GenerateRows(List<TsFormField> fields)
    {
        var byCSharpName = fields.ToDictionary(x => x.CSharpName);
        var emitted = new HashSet<string>();

        var akaFiled = fields.FirstOrDefault(x => x.CSharpName == nameof(SpeciesSettings.Aka));

        foreach (var field in fields)
        {
            if (emitted.Contains(field.CSharpName))
                continue;

            if (field.IsVariance)
                continue;

            // if (field.Kind == "behavior")
            // {
            //     foreach (var line in GenerateBehaviorRow(field))
            //         yield return line;

            //     emitted.Add(field.CSharpName);
            //     continue;
            // }

            // if (field.Kind == "checkbox")
            // {
            //     foreach (var line in GenerateCheckboxRow(field))
            //         yield return line;

            //     emitted.Add(field.CSharpName);
            //     continue;
            // }

            // if (field.Kind == "text")
            // {
            //     foreach (var line in GenerateTextRow(field))
            //         yield return line;

            //     emitted.Add(field.CSharpName);
            //     continue;
            // }

            var varianceName = field.CSharpName + "Var";

            // if (byCSharpName.TryGetValue(varianceName, out var variance))
            // {
            //     foreach (var line in GenerateNumberWithVarianceRow(field, variance))
            //         yield return line;

            //     emitted.Add(field.CSharpName);
            //     emitted.Add(variance.CSharpName);
            // }
            // else
            {
                switch (field.CSharpName)
                {
                    case nameof(SpeciesSettings.Name): yield return GenerateNameRow(field, akaFiled); break;
                    case nameof(SpeciesSettings.Aka): yield return ""; break;
                    case nameof(SpeciesSettings.Behavior): yield return GenerateBehaviorRow(); break;
                    default: yield return GenerateGenericRow(field); break;
                }

                emitted.Add(field.CSharpName);
            }

            // if (ShouldInsertSeparatorAfter(field.CSharpName))
            //     yield return "        <hr />";
        }
    }
}
