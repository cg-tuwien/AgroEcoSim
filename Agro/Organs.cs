namespace Agro;

//TODO IMPORTANT All resource transport should be request-confirm messages, i.e. pull-policy.
//  There should never be forced resource push since it may not be able to fit into the available storage.
//TODO Create properties computing the respective storages for water and energy. Just for clarity.
//[Flags]  //flags are not needed anymore

public enum OrganTypes : byte { Unspecified = 0, Seed = 1, Bud = 2, Root = 4, Stem = 8, Leaf = 16, Fruit = 32 };