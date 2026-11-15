namespace SpoitableToolKit.Host.Config.Binding;

internal sealed record CollectedConfigMembers(IReadOnlyList<ConfigMember> Members, IReadOnlyList<ConfigMember> Order);
