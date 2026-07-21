/// <summary>
/// Combat interface for all entities that can receive damage.
///
/// The player's DamageHandler and the boss's BossController both implement this.
/// Player skills (BurstStrikeSkill, ProjectileBehavior) call GetComponent/GetComponentInParent
/// to resolve this interface on their hit targets.
///
/// Do not rename or remove members — HUD, combat, and behavior tracking systems depend on this contract.
/// </summary>
public interface IDamageable
{
    void TakeDamage(float damage);
}
