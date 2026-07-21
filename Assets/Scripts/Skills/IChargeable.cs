/// <summary>
/// Optional interface for skills that use discrete charges rather than a single cooldown.
/// CooldownUI checks for this to display a charge count label alongside the fill bar.
/// ISkill is not extended — boss skills do not implement this.
/// </summary>
public interface IChargeable
{
    int ChargesRemaining { get; }
    int MaxCharges       { get; }
}
