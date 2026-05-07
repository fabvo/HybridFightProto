using UnityEngine;

/// <summary>
/// Available NFC tag effects. The default effect (when a tag has no NDEF record
/// or an unknown one) is Focus, which preserves the original charging behavior.
/// </summary>
public enum TagEffect { Focus, Heal, Shield, Bomb }

/// <summary>
/// Centralized definitions for everything tag-effect related: parsing, display names,
/// colors and tunables. Both GameManager (logic) and UIManager (visuals) read from
/// here -- there's no duplication of effect knowledge anywhere else.
///
/// To write a tag with NFC Tools PRO:
///   1. Open NFC Tools PRO -> tab "Schreiben" / "Write"
///   2. "Datensatz hinzufuegen" -> "Text"
///   3. Text eingeben: FOCUS, HEAL, SHIELD oder BOMB (Gross-/Kleinschreibung egal)
///   4. "Schreiben" und Tag ans Handy halten
///
/// Tags ohne NDEF-Record verhalten sich automatisch wie FOCUS-Tags.
/// </summary>
public static class TagEffects
{
    // ------------ Tunables ------------
    public const float HealHpPerSecond = 8f;
    public const int   BombDamage      = 30;

    // ------------ Parsing ------------
    public static TagEffect Parse(string ndefPayload)
    {
        if (string.IsNullOrEmpty(ndefPayload)) return TagEffect.Focus;
        switch (ndefPayload.Trim().ToUpperInvariant())
        {
            case "HEAL":   return TagEffect.Heal;
            case "SHIELD": return TagEffect.Shield;
            case "BOMB":   return TagEffect.Bomb;
            case "FOCUS":  return TagEffect.Focus;
            default:       return TagEffect.Focus;
        }
    }

    // ------------ Display ------------
    public static string DisplayName(TagEffect e)
    {
        switch (e)
        {
            case TagEffect.Heal:   return "HEILUNG";
            case TagEffect.Shield: return "SCHILD";
            case TagEffect.Bomb:   return "BOMBE";
            default:               return "FOKUS";
        }
    }

    public static string Description(TagEffect e)
    {
        switch (e)
        {
            case TagEffect.Heal:   return $"Heilt dich ({HealHpPerSecond:0} HP/s) solange aufliegt";
            case TagEffect.Shield: return "Blockiert eingehenden Schaden komplett";
            case TagEffect.Bomb:   return $"Sofort-Angriff fuer {BombDamage} Schaden!";
            default:               return "Lade auf - naechster Angriff macht mehr Schaden";
        }
    }

    public static Color Color(TagEffect e)
    {
        switch (e)
        {
            case TagEffect.Heal:   return new Color(0.40f, 1.00f, 0.50f);
            case TagEffect.Shield: return new Color(0.40f, 0.70f, 1.00f);
            case TagEffect.Bomb:   return new Color(1.00f, 0.40f, 0.40f);
            default:               return new Color(1.00f, 0.90f, 0.30f);
        }
    }
}
