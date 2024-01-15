namespace Agro;

public enum OrganTypes : byte {
    Unspecified = 0,
    /// <summary>
    /// The seed as a whole, the plant has no other organs at that time
    /// </summary>
    Seed,
    /// <summary>
    /// Dormant bud that will eventually develop into a new twig. Buds can only be attached to stems
    /// </summary>
    Bud,
    /// <summary>
    /// Root segment
    /// </summary>
    Root,
    /// <summary>
    /// Stem segment
    /// </summary>
    Stem,
    /// <summary>
    /// Leaf (excl. the connection to the stem)
    /// </summary>
    Leaf,
    /// <summary>
    /// Connection between the stem and the leaf
    /// </summary>
    Petiole,
    /// <summary>
    /// Fruit (not used yet)
    /// </summary>
    Fruit,
    /// <summary>
    /// Tip of the stem where cells grow forward elongating the twig
    /// </summary>
    Meristem
};