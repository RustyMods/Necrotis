namespace Necrotis.Managers;

public class CreatureStatus : StatusEffect
{
    public FaunaManager.Critter.StatusEffectData m_data = null!;

    public override void OnDamaged(HitData hit, Character attacker)
    {
        hit.ApplyModifier(m_data.m_armorMultiplier.Value);
    }

    public override void ModifyAttack(Skills.SkillType skill, ref HitData hitData)
    {
        hitData.ApplyModifier(m_data.m_damageMultiplier.Value);
    }
}