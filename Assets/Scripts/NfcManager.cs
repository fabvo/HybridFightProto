using UnityEngine;

/// <summary>
/// Receives NFC messages from the Java plugin in the format "uid|ndefPayload".
/// Derives TagPresent (via heartbeat freshness) and CurrentEffect (via TagEffects.Parse).
///
/// MUST sit on a GameObject named "NfcManager" (UnitySendMessage routing).
/// </summary>
public class NfcManager : MonoBehaviour
{
    public const float PresenceWindow = 0.6f;

    public string    LastTagId         { get; private set; }
    public string    LastNdefPayload   { get; private set; } = "";
    public TagEffect CurrentEffect     { get; private set; } = TagEffect.Focus;
    public float     LastTagTime       { get; private set; } = -999f;
    public int       TagsDetectedCount { get; private set; }
    public string    LastError         { get; private set; }

    public bool  TagPresent         => LastTagId != null && Time.time - LastTagTime < PresenceWindow;
    public float SecondsSinceLastTag => Time.time - LastTagTime;

    public event System.Action<string> OnTagDiscovered;
    public event System.Action<string> OnPluginError;

    /// <summary>Called from Java. Format: "UID_HEX|NDEF_TEXT" (pipe-separated).</summary>
    public void OnNfcTagDiscovered(string message)
    {
        // Parse uid|payload
        string uid;
        string payload;
        int pipe = message.IndexOf('|');
        if (pipe >= 0)
        {
            uid     = message.Substring(0, pipe);
            payload = message.Substring(pipe + 1);
        }
        else
        {
            uid     = message;
            payload = "";
        }

        bool isFirstThisSession = LastTagId == null || !TagPresent;
        LastTagId       = uid;
        LastNdefPayload = payload;
        CurrentEffect   = TagEffects.Parse(payload);
        LastTagTime     = Time.time;

        if (isFirstThisSession)
        {
            TagsDetectedCount++;
            Debug.Log($"[NFC] Tag #{TagsDetectedCount} placed: {uid}, NDEF=\"{payload}\", Effect={CurrentEffect}");
        }
        OnTagDiscovered?.Invoke(uid);
    }

    public void OnNfcStatus(string statusMessage)
    {
        Debug.Log($"[NFC] Status: {statusMessage}");
        if (statusMessage.StartsWith("ERROR"))
        {
            LastError = statusMessage;
            OnPluginError?.Invoke(statusMessage);
        }
    }
}
