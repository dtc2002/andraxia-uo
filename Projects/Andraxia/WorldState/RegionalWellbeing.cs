namespace Server.Andraxia;

public enum RegionalSecurityClassification { Lawless, Unstable, Secure, WellGuarded }
public enum RegionalProsperityClassification { Impoverished, Struggling, Prosperous, Thriving }
public readonly record struct RegionalValueChange(int Delta, string Reason);

public static class RegionalSecurity
{
    public static RegionalSecurityClassification Classify(int value) => value switch
    {
        <= 24 => RegionalSecurityClassification.Lawless,
        <= 49 => RegionalSecurityClassification.Unstable,
        <= 74 => RegionalSecurityClassification.Secure,
        _ => RegionalSecurityClassification.WellGuarded
    };

    public static string Label(RegionalSecurityClassification value) => value switch
    {
        RegionalSecurityClassification.Lawless => "Lawless",
        RegionalSecurityClassification.Unstable => "Unstable",
        RegionalSecurityClassification.Secure => "Secure",
        _ => "Well Guarded"
    };

    public static string Description(RegionalSecurityClassification value) => value switch
    {
        RegionalSecurityClassification.Lawless => "Local protection is largely ineffective.",
        RegionalSecurityClassification.Unstable => "Protection is inconsistent and danger remains common.",
        RegionalSecurityClassification.Secure => "Local institutions provide dependable protection.",
        _ => "The region is strongly protected and well defended."
    };
}

public static class RegionalProsperity
{
    public static RegionalProsperityClassification Classify(int value) => value switch
    {
        <= 24 => RegionalProsperityClassification.Impoverished,
        <= 49 => RegionalProsperityClassification.Struggling,
        <= 74 => RegionalProsperityClassification.Prosperous,
        _ => RegionalProsperityClassification.Thriving
    };

    public static string Label(RegionalProsperityClassification value) => value.ToString();

    public static string Description(RegionalProsperityClassification value) => value switch
    {
        RegionalProsperityClassification.Impoverished => "Local commerce and livelihoods are in severe decline.",
        RegionalProsperityClassification.Struggling => "The regional economy remains under strain.",
        RegionalProsperityClassification.Prosperous => "Commerce and livelihoods are generally healthy.",
        _ => "The region enjoys vigorous commerce and broad prosperity."
    };
}
