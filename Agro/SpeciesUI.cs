namespace Agro;

[AttributeUsage(AttributeTargets.Property)]
public class SpeciesUILabelAttribute(string value) : Attribute
{
    public string Value { get; } = value;
}

[AttributeUsage(AttributeTargets.Property)]
public class SpeciesUIGroupAttribute(string value) : Attribute
{
    public string Value { get; } = value;
}

[AttributeUsage(AttributeTargets.Property)]
public class SpeciesUITooltipAttribute(string value) : Attribute
{
    public string Value { get; } = value;
}

[AttributeUsage(AttributeTargets.Property)]
public class SpeciesUIIgnoreAttribute() : Attribute { }