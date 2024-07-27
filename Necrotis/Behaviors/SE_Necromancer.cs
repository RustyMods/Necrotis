namespace Necrotis.Behaviors;

public class SE_Necromancer : SE_Stats
{
    public override void Setup(Character character)
    {
        m_icon = ObjectDB.instance.GetItemPrefab("TrophyNecromancer").GetComponent<ItemDrop>().m_itemData.GetIcon();
        base.Setup(character);
        
    }
}