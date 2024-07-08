using System.Text;
using BepInEx.Configuration;

namespace Necrotis.Behaviors;

public class HeartSE : StatusEffect
{
    public ConfigEntry<float> m_burnTime = null!;
    public ConfigEntry<float> m_carryWeight = null!;
    public ConfigEntry<float> m_healthRegen = null!;
    public ConfigEntry<float> m_eitrRegen = null!;
    public ConfigEntry<float> m_stamRegen = null!;

    public override void Setup(Character character)
    {
        m_ttl = m_burnTime.Value;
        base.Setup(character);
    }

    public override void ModifyMaxCarryWeight(float baseLimit, ref float limit)
    {
        limit += m_carryWeight.Value;
    }

    public override void ModifyEitrRegen(ref float eitrRegen)
    {
        eitrRegen *= m_eitrRegen.Value;
    }

    public override void ModifyHealthRegen(ref float regenMultiplier)
    {
        regenMultiplier *= m_healthRegen.Value;
    }

    public override void ModifyStaminaRegen(ref float staminaRegen)
    {
        staminaRegen *= m_stamRegen.Value;
    }

    public override string GetTooltipString()
    {
        StringBuilder stringBuilder = new StringBuilder();
        if (m_healthRegen.Value != 0f)
        {
            stringBuilder.AppendFormat("$se_healthregen: <color=orange>{0}%</color>\n", (m_healthRegen.Value - 1) * 100f);
        }
        if (m_stamRegen.Value != 0f)
        {
            stringBuilder.AppendFormat("$se_staminaregen: <color=orange>{0}%</color>\n", (m_stamRegen.Value - 1) * 100f);
        }

        if (m_eitrRegen.Value != 0f)
        {
            stringBuilder.AppendFormat("$se_eitrregen: <color=orange>{0}%</color>\n", (m_eitrRegen.Value - 1) * 100f);
        }

        if (m_carryWeight.Value != 0f)
        {
            stringBuilder.AppendFormat("$se_max_carryweight: <color=orange>{0:+0;-0}</color>", m_carryWeight.Value);
        }
    
        return Localization.instance.Localize(stringBuilder.ToString());
    }
}