namespace HerdDesk.Contracts;

// Compare and display with Raw. Side-effect branches use Known plus VerifiedOperations.
public readonly record struct WireEnum<TKnown>(string Raw, TKnown? Known)
    where TKnown : struct, Enum;
