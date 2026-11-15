namespace SpoitableToolKit.Abstractions.Config.Attributes;

/// <summary>
/// <b>そのセクションが切でも出す。</b>
/// <para>
/// <see cref="IConditionalSettings.IsEnabled"/> を決めているスイッチ自体が
/// セクションの中にあると、切にした瞬間にスイッチごと消えて<b>二度と戻せなくなる</b>。
/// その 1 つだけを出しっぱなしにするための印。
/// </para>
/// <para>
/// スイッチが親の側にある形（<c>owner.Mode</c> を見るなど）では要らない。
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class SpoitableSettingsToggleAttribute : Attribute;
