namespace Agro;

public enum OrganTypes : byte {
    Unspecified = 0,
    /// <summary>
    /// The seed as a whole, the plant has no other organs at that time
    /// </summary>
    Seed, //1
    /// <summary>
    /// Dormant bud that will eventually develop into a new twig. Buds can only be attached to stems
    /// </summary>
    Bud, //2
    /// <summary>
    /// Root segment
    /// </summary>
    Root, //3
    /// <summary>
    /// Stem segment
    /// </summary>
    Stem, //4
    /// <summary>
    /// Leaf (excl. the connection to the stem)
    /// </summary>
    Leaf, //5
    /// <summary>
    /// Connection between the stem and the leaf
    /// </summary>
    Petiole, //6
    /// <summary>
    /// Fruit (not used yet)
    /// </summary>
    Fruit, //7
    /// <summary>
    /// Tip of the stem where cells grow forward elongating the twig
    /// </summary>
    Meristem, //8
    RizomeMeristem, //9
    FlowerBud, //10 Knospe
    FlowerStem, //11 kleiner Bl�tenstiel
    FlowerPadel, //12 eigentliche Bl�te
    FlowerPedicel, //13
    FlowerMeristem, //14
    FlowerBaseBud, //15
};