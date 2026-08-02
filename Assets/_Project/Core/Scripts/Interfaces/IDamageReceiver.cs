/// <summary>
/// SOLID — ISP / DIP:
/// Any object that can receive damage implements this interface.
/// Callers (MeleeHitbox, GirlAttackNet, etc.) depend on this abstraction,
/// not on the concrete TargetHealth class.
/// </summary>
public interface IDamageReceiver
{
    /// <summary>
    /// Apply damage to this entity.
    /// Must only be called on the Server.
    /// </summary>
    /// <param name="amount">Damage amount.</param>
    /// <param name="isSoulAttack">True for demon soul damage, false for physical.</param>
    void TakeDamage(float amount, bool isSoulAttack = false);
}
